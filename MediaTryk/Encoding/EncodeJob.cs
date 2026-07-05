namespace MediaTryk.Encoding;

public enum EncodeJobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Canceled
}

public class EncodeJob
{
    public required Guid Id { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public EncodeJobStatus Status { get; set; } = EncodeJobStatus.Queued;
    public double? Progress { get; set; }
    public double? EtaSeconds { get; set; }
    public required DateTimeOffset QueuedAt { get; init; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
