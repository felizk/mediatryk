using MediaTryk.Media;

namespace MediaTryk.Encoding;

/// <summary>
/// Processes queued encode jobs one at a time in the background, resolving each
/// job's source and destination paths and handing the actual conversion off to IVideoEncoder.
/// </summary>
public class EncodeQueueHostedService(
    EncodeQueue queue,
    SourcePathResolver sourceResolver,
    MediaPathResolver mediaResolver,
    IVideoEncoder encoder,
    ILogger<EncodeQueueHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.ReadAllAsync(stoppingToken))
        {
            job.Status = EncodeJobStatus.Running;
            job.StartedAt = DateTimeOffset.UtcNow;

            try
            {
                if (!sourceResolver.TryResolve(job.SourcePath, out var sourceFullPath) || !File.Exists(sourceFullPath))
                {
                    throw new FileNotFoundException("Source file no longer exists.", job.SourcePath);
                }

                var destinationFullPath = Path.Combine(mediaResolver.RootPath, job.DestinationPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFullPath)!);

                await encoder.EncodeAsync(sourceFullPath, destinationFullPath, stoppingToken);

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
                job.CompletedAt = DateTimeOffset.UtcNow;
            }
        }
    }
}
