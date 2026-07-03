namespace MediaTryk.Encoding;

public record EncodeQueueRequestDto(string Path);

public record EncodeJobDto(
    Guid Id,
    string SourcePath,
    string Status,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage);

public static class EncodeJobExtensions
{
    public static EncodeJobDto ToDto(this EncodeJob job) => new(
        job.Id,
        job.SourcePath,
        job.Status.ToString(),
        job.QueuedAt,
        job.StartedAt,
        job.CompletedAt,
        job.ErrorMessage);
}
