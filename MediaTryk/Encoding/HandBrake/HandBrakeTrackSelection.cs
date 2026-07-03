namespace MediaTryk.Encoding.HandBrake;

/// <summary>
/// Audio/subtitle tracks to include in an encode, as 1-based indices within
/// their type - matching HandBrakeCLI's own -a/-s numbering.
/// </summary>
public record HandBrakeTrackSelection(
    IReadOnlyList<int> AudioTrackIndices,
    IReadOnlyList<int> SubtitleTrackIndices);
