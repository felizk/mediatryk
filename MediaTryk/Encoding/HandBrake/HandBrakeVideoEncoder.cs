using System.Diagnostics;
using MediaTryk.Encoding.Mkv;
using Microsoft.Extensions.Options;

namespace MediaTryk.Encoding.HandBrake;

/// <summary>
/// Identifies the source MKV's tracks via mkvmerge, selects the default ones,
/// and runs HandBrakeCLI with the fixed HandBrakeEncodeProfile settings, using
/// the Intel QSV encoder when it's enabled and available.
/// </summary>
public partial class HandBrakeVideoEncoder(
    MkvMergeIdentifier mkvMergeIdentifier,
    HandBrakeCapabilities capabilities,
    IOptions<HandBrakeOptions> options,
    ILogger<HandBrakeVideoEncoder> logger) : IVideoEncoder
{
    public async Task EncodeAsync(
        string sourceFullPath,
        string destinationFullPath,
        Action<EncodeProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        var tracks = await mkvMergeIdentifier.IdentifyTracksAsync(sourceFullPath, cancellationToken);
        var selection = TrackSelector.Select(tracks);

        var useHardwareEncoder = options.Value.EnableHardwareEncoding
            && await capabilities.IsQsvAvailableAsync();
        var args = HandBrakeArgumentBuilder.Build(
            sourceFullPath, destinationFullPath, selection, useHardwareEncoder);

        LogEncoding(
            sourceFullPath,
            useHardwareEncoder
                ? HandBrakeEncodeProfile.HardwareVideoEncoder
                : HandBrakeEncodeProfile.VideoEncoder);

        var startInfo = new ProcessStartInfo
        {
            FileName = "HandBrakeCLI",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        // Switches stdout to structured JSON state blocks so progress can be parsed.
        startInfo.ArgumentList.Add("--json");
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start HandBrakeCLI.");

        var stdoutTask = ReadProgressAsync(process, onProgress, cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Encoding {SourcePath} with {Encoder}.")]
    private partial void LogEncoding(string sourcePath, string encoder);

    private static async Task ReadProgressAsync(
        Process process,
        Action<EncodeProgress>? onProgress,
        CancellationToken cancellationToken)
    {
        var parser = new HandBrakeProgressParser();

        while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            if (parser.ProcessLine(line) is { } progress)
            {
                onProgress?.Invoke(progress);
            }
        }
    }
}
