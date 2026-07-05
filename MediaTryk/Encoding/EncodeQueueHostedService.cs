using System.Diagnostics;
using MediaTryk.Media;

namespace MediaTryk.Encoding;

/// <summary>
/// Processes queued encode jobs one at a time in the background, resolving each
/// job's source and destination paths and handing the actual conversion off to IVideoEncoder.
/// </summary>
public class EncodeQueueHostedService(
    EncodeQueue queue,
    MediaPathResolver resolver,
    IVideoEncoder encoder,
    ILogger<EncodeQueueHostedService> logger) : BackgroundService
{
    // Encoded under this suffix so an in-progress file is never mistaken for a
    // finished one; it also falls outside MediaFile's allowed extensions, so it
    // won't show up via the media API while encoding is still in flight.
    private const string InProgressSuffix = ".encoding";

    // Progress broadcasts are rate-limited so a fast encode doesn't flood
    // WebSocket subscribers with hundreds of messages per second.
    private static readonly TimeSpan ProgressNotifyInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.ReadAllAsync(stoppingToken))
        {
            // The job object is the lock guarding its Queued -> Running/Canceled
            // transition, shared with EncodeQueue.Cancel.
            lock (job)
            {
                if (job.Status == EncodeJobStatus.Canceled)
                {
                    continue;
                }

                job.Status = EncodeJobStatus.Running;
                job.StartedAt = DateTimeOffset.UtcNow;
            }

            using var jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            job.Cancellation = jobCancellation;
            queue.NotifyChanged(job);
            queue.PersistState();

            var destinationFullPath = Path.Combine(resolver.MediaRootPath, job.DestinationPath);
            var inProgressFullPath = destinationFullPath + InProgressSuffix;

            try
            {
                jobCancellation.Token.ThrowIfCancellationRequested();

                if (!resolver.TryResolveSource(job.SourcePath, out var sourceFullPath) || !File.Exists(sourceFullPath))
                {
                    throw new FileNotFoundException("Source file no longer exists.", job.SourcePath);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationFullPath)!);

                var lastNotify = Stopwatch.StartNew();
                await encoder.EncodeAsync(sourceFullPath, inProgressFullPath, progress =>
                {
                    job.Progress = progress.FractionComplete;
                    job.EtaSeconds = progress.EtaSeconds;

                    if (lastNotify.Elapsed >= ProgressNotifyInterval)
                    {
                        lastNotify.Restart();
                        queue.NotifyChanged(job);
                    }
                }, jobCancellation.Token);

                File.Move(inProgressFullPath, destinationFullPath, overwrite: true);

                job.Status = EncodeJobStatus.Completed;
                job.Progress = 1;
                job.EtaSeconds = null;
            }
            catch (OperationCanceledException) when (jobCancellation.IsCancellationRequested)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    // Server shutdown, not a user cancel: put the job back to
                    // Queued so it's persisted as pending and resumes on the
                    // next start.
                    job.Status = EncodeJobStatus.Queued;
                    job.StartedAt = null;
                    job.Progress = null;
                }
                else
                {
                    job.Status = EncodeJobStatus.Canceled;
                }

                job.EtaSeconds = null;
            }
            catch (Exception ex)
            {
                job.Status = EncodeJobStatus.Failed;
                job.ErrorMessage = ex.Message;
                logger.LogError(ex, "Encode failed for job {JobId} ({SourcePath})", job.Id, job.SourcePath);
            }
            finally
            {
                job.Cancellation = null;
                TryDeleteInProgressFile(inProgressFullPath);
                PruneEmptyDestinationDirectories(destinationFullPath);

                if (job.Status != EncodeJobStatus.Queued)
                {
                    job.CompletedAt = DateTimeOffset.UtcNow;
                }

                queue.NotifyChanged(job);
                queue.PersistState();
            }
        }
    }

    private void TryDeleteInProgressFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean up leftover in-progress file {Path}", path);
        }
    }

    // Removes destination directories left empty after a canceled or failed
    // encode, walking upward until a non-empty directory or the media root
    // (which is never deleted). A completed encode's directory contains the
    // output file, so pruning stops immediately there.
    private void PruneEmptyDestinationDirectories(string destinationFullPath)
    {
        var root = Path.GetFullPath(resolver.MediaRootPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationFullPath));

        while (directory is not null
            && directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            if (Directory.Exists(directory))
            {
                try
                {
                    // Non-recursive: throws IOException if the directory has
                    // any content, which is the stop condition.
                    Directory.Delete(directory);
                }
                catch (IOException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to prune empty media directory {Path}", directory);
                    break;
                }
            }

            directory = Path.GetDirectoryName(directory);
        }
    }
}
