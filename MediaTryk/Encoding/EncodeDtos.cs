namespace MediaTryk.Encoding;

public record EncodeQueueRequestDto(string Path);

public record EncodeJobDto(
    Guid Id,
    string SourcePath,
    string DestinationPath,
    string Status,
    long Order,
    double? Progress,
    double? EtaSeconds,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage);

public static class EncodeJobExtensions
{
    public static EncodeJobDto ToDto(this EncodeJob job) => new(
        job.Id,
        job.SourcePath,
        job.DestinationPath,
        job.Status.ToString(),
        job.Order,
        job.Progress,
        job.EtaSeconds,
        job.QueuedAt,
        job.StartedAt,
        job.CompletedAt,
        job.ErrorMessage);
}
