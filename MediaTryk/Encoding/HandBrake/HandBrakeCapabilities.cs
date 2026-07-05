using System.Diagnostics;

namespace MediaTryk.Encoding.HandBrake;

/// <summary>
/// Probes HandBrakeCLI once, on first use, for Intel QSV hardware encoder
/// availability. HandBrakeCLI only lists the qsv encoders in its --help output
/// when the build supports QSV and the GPU/driver is actually usable, so a
/// missing /dev/dri device or media driver simply results in the software
/// encoder being used.
/// </summary>
public partial class HandBrakeCapabilities(ILogger<HandBrakeCapabilities> logger)
{
    private readonly Lazy<Task<bool>> _qsvAvailable = new(
        () => Task.Run(() => ProbeQsvAsync(logger)),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public Task<bool> IsQsvAvailableAsync() => _qsvAvailable.Value;

    private static async Task<bool> ProbeQsvAsync(ILogger logger)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "HandBrakeCLI",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("--help");

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start HandBrakeCLI.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var output = await stdoutTask + await stderrTask;
            var available = output.Contains(
                HandBrakeEncodeProfile.HardwareVideoEncoder, StringComparison.Ordinal);

            LogProbeResult(
                logger,
                available,
                available
                    ? HandBrakeEncodeProfile.HardwareVideoEncoder
                    : HandBrakeEncodeProfile.VideoEncoder);

            return available;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "HandBrakeCLI capability probe failed; falling back to software encoding.");
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Intel QSV available: {QsvAvailable}; encodes will use {Encoder}.")]
    private static partial void LogProbeResult(ILogger logger, bool qsvAvailable, string encoder);
}
