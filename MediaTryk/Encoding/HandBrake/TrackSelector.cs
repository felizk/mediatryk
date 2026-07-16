using MediaTryk.Encoding.Mkv;

namespace MediaTryk.Encoding.HandBrake;

/// <summary>
/// Picks which tracks HandBrake should include in an encode: the tracks flagged
/// default in the source MKV, per type. Falls back to the first audio track when
/// none is flagged default, since an encode needs at least one audio track.
/// Falls back to the first English subtitle track when none is flagged default.
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

        var subtitleTracks = tracks.Where(t => t.Type == MkvTrackType.Subtitles).ToList();
        var subtitleIndices = subtitleTracks
            .Where(t => t.IsDefault)
            .Select(t => t.TypeIndex)
            .ToList();

        if (subtitleIndices.Count == 0)
        {
            var firstEnglish = subtitleTracks.FirstOrDefault(t => t.Language is "eng" or "en");
            if (firstEnglish is not null)
            {
                subtitleIndices = [firstEnglish.TypeIndex];
            }
        }

        return new HandBrakeTrackSelection(audioIndices, subtitleIndices);
    }
}
