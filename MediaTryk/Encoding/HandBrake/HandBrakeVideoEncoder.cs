using System.Diagnostics;
using MediaTryk.Encoding.Mkv;

namespace MediaTryk.Encoding.HandBrake;

/// <summary>
/// Identifies the source MKV's tracks via mkvmerge, selects the default ones,
/// and runs HandBrakeCLI with the fixed HandBrakeEncodeProfile settings.
/// </summary>
public class HandBrakeVideoEncoder(
    MkvMergeIdentifier mkvMergeIdentifier,
    ILogger<HandBrakeVideoEncoder> logger) : IVideoEncoder
{
    public async Task EncodeAsync(string sourceFullPath, string destinationFullPath, CancellationToken cancellationToken)
    {
        var tracks = await mkvMergeIdentifier.IdentifyTracksAsync(sourceFullPath, cancellationToken);
        var selection = TrackSelector.Select(tracks);
        var args = HandBrakeArgumentBuilder.Build(sourceFullPath, destinationFullPath, selection);

        var startInfo = new ProcessStartInfo
        {
            FileName = "HandBrakeCLI",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start HandBrakeCLI.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        var stderr = await stderrTask;
        await stdoutTask;

        if (process.ExitCode != 0)
        {
            logger.LogError(
                "HandBrakeCLI exited with code {ExitCode} for {SourcePath}: {Stderr}",
                process.ExitCode, sourceFullPath, stderr);
            throw new InvalidOperationException($"HandBrakeCLI exited with code {process.ExitCode}.");
        }
    }
}
