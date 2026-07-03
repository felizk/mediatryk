namespace MediaTryk.Encoding;

/// <summary>
/// Performs the actual source-to-media conversion for a single job.
/// The real implementation will shell out to HandBrake; for now a stub stands in.
/// </summary>
public interface IVideoEncoder
{
    Task EncodeAsync(string sourceFullPath, string destinationFullPath, CancellationToken cancellationToken);
}
