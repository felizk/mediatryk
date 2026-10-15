using MediaTryk.Encoding;
using MediaTryk.Encoding.HandBrake;

namespace MediaTryk.Media;

/// <summary>Status/size rules shared by browse and the batch status endpoint.</summary>
public static class MediaEncodeStatus
{
    public static (MediaFileEncodeStatus Status, long SizeBytes) Evaluate(
        MediaPathResolver resolver, EncodeQueue queue, string sourceRelativePath, long sourceLength)
    {
        var encodedFile = new FileInfo(Path.Combine(
            resolver.MediaRootPath,
            Path.ChangeExtension(sourceRelativePath, HandBrakeEncodeProfile.OutputExtension)));

        var status = encodedFile.Exists ? MediaFileEncodeStatus.Encoded
            : queue.IsActive(sourceRelativePath) ? MediaFileEncodeStatus.Encoding
            : MediaFileEncodeStatus.NotEncoded;

        // Once encoded, the size that matters is the streamable file's.
        return (status, encodedFile.Exists ? encodedFile.Length : sourceLength);
    }
}
