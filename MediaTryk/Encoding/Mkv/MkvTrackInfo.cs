namespace MediaTryk.Encoding.Mkv;

public enum MkvTrackType
{
    Video,
    Audio,
    Subtitles
}

/// <summary>
/// A single track from an MKV file, as reported by `mkvmerge -J`.
/// </summary>
/// <param name="Id">The track's global id within the file (mkvmerge's "id").</param>
/// <param name="TypeIndex">1-based index of this track among tracks of the same Type, in file order.
/// This matches HandBrakeCLI's own per-type track numbering (e.g. -a/-s indices).</param>
public record MkvTrackInfo(
    int Id,
    MkvTrackType Type,
    int TypeIndex,
    string? Codec,
    string? Language,
    string? Name,
    bool IsDefault,
    bool IsForced,
    bool IsEnabled);
