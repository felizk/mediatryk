using MediaTryk.Encoding.Mkv;

namespace MediaTryk.Encoding.HandBrake;

/// <summary>
/// Picks which tracks HandBrake should include in an encode: the tracks flagged
/// default in the source MKV, per type. Falls back to the first audio track when
/// none is flagged default, since an encode needs at least one audio track.
/// Video track selection is left to HandBrake itself - it only encodes one video
/// track per source, and source video tracks are rarely flagged default.
/// </summary>
public static class TrackSelector
{
    public static HandBrakeTrackSelection Select(IReadOnlyList<MkvTrackInfo> tracks)
    {
        if (tracks.All(t => t.Type != MkvTrackType.Video))
        {
            throw new InvalidOperationException("Source file has no video track.");
        }

        var audioTracks = tracks.Where(t => t.Type == MkvTrackType.Audio).ToList();
        var audioIndices = audioTracks
            .Where(t => t.IsDefault)
            .Select(t => t.TypeIndex)
            .ToList();

        if (audioIndices.Count == 0 && audioTracks.Count > 0)
        {
            audioIndices = [audioTracks[0].TypeIndex];
        }

        var subtitleIndices = tracks
            .Where(t => t.Type == MkvTrackType.Subtitles && t.IsDefault)
            .Select(t => t.TypeIndex)
            .ToList();

        return new HandBrakeTrackSelection(audioIndices, subtitleIndices);
    }
}
