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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.ReadAllAsync(stoppingToken))
        {
            job.Status = EncodeJobStatus.Running;
            job.StartedAt = DateTimeOffset.UtcNow;

            var destinationFullPath = Path.Combine(resolver.MediaRootPath, job.DestinationPath);
            var inProgressFullPath = destinationFullPath + InProgressSuffix;

            try
            {
                if (!resolver.TryResolveSource(job.SourcePath, out var sourceFullPath) || !File.Exists(sourceFullPath))
                {
                    throw new FileNotFoundException("Source file no longer exists.", job.SourcePath);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationFullPath)!);

                await encoder.EncodeAsync(sourceFullPath, inProgressFullPath, stoppingToken);

                File.Move(inProgressFullPath, destinationFullPath, overwrite: true);

                job.Status = EncodeJobStatus.Completed;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                job.Status = EncodeJobStatus.Canceled;
            }
            catch (Exception ex)
            {
                job.Status = EncodeJobStatus.Failed;
                job.ErrorMessage = ex.Message;
                logger.LogError(ex, "Encode failed for job {JobId} ({SourcePath})", job.Id, job.SourcePath);
            }
            finally
            {
                TryDeleteInProgressFile(inProgressFullPath);
                job.CompletedAt = DateTimeOffset.UtcNow;
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
}
