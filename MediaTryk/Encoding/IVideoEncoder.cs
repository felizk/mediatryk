namespace MediaTryk.Encoding;

public record EncodeProgress(double FractionComplete, double? EtaSeconds);

/// <summary>
/// Performs the actual source-to-media conversion for a single job,
/// reporting progress through the optional callback as the encode runs.
/// </summary>
public interface IVideoEncoder
{
    Task EncodeAsync(
        string sourceFullPath,
        string destinationFullPath,
        Action<EncodeProgress>? onProgress,
        CancellationToken cancellationToken);
}
