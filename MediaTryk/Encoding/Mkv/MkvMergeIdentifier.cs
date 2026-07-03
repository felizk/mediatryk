using System.Diagnostics;
using System.Text.Json;

namespace MediaTryk.Encoding.Mkv;

/// <summary>
/// Shells out to `mkvmerge -J` to identify the tracks contained in an MKV file.
/// </summary>
public class MkvMergeIdentifier
{
    public async Task<IReadOnlyList<MkvTrackInfo>> IdentifyTracksAsync(string filePath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "mkvmerge",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-J");
        startInfo.ArgumentList.Add(filePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start mkvmerge.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        // mkvmerge exit codes: 0 = ok, 1 = ok with warnings, 2 = error.
        if (process.ExitCode >= 2)
        {
            throw new InvalidOperationException($"mkvmerge exited with code {process.ExitCode}: {stderr}");
        }

        var result = JsonSerializer.Deserialize<MkvMergeIdentifyResult>(stdout)
            ?? throw new InvalidOperationException("mkvmerge produced no output.");

        var typeCounts = new Dictionary<MkvTrackType, int>();
        var tracks = new List<MkvTrackInfo>();

        foreach (var track in result.Tracks)
        {
            if (!TryParseType(track.Type, out var type))
            {
                continue;
            }

            typeCounts[type] = typeCounts.GetValueOrDefault(type) + 1;

            tracks.Add(new MkvTrackInfo(
                track.Id,
                type,
                typeCounts[type],
                track.Codec,
                track.Properties.Language,
                track.Properties.TrackName,
                track.Properties.DefaultTrack,
                track.Properties.ForcedTrack,
                track.Properties.EnabledTrack));
        }

        return tracks;
    }

    private static bool TryParseType(string type, out MkvTrackType result)
    {
        switch (type)
        {
            case "video":
                result = MkvTrackType.Video;
                return true;
            case "audio":
                result = MkvTrackType.Audio;
                return true;
            case "subtitles":
                result = MkvTrackType.Subtitles;
                return true;
            default:
                result = default;
                return false;
        }
    }
}
