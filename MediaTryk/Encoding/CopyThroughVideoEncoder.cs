namespace MediaTryk.Encoding;

/// <summary>
/// Placeholder encoder used until HandBrake is wired in: copies the source file
/// into the media library unchanged so the queue is testable end to end.
/// </summary>
public class CopyThroughVideoEncoder : IVideoEncoder
{
    public Task EncodeAsync(string sourceFullPath, string destinationFullPath, CancellationToken cancellationToken) =>
        Task.Run(() => File.Copy(sourceFullPath, destinationFullPath, overwrite: true), cancellationToken);
}
