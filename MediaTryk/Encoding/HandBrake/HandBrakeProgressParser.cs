using System.Text;
using System.Text.Json;

namespace MediaTryk.Encoding.HandBrake;

/// <summary>
/// Incrementally parses HandBrakeCLI --json stdout. State arrives as multi-line
/// blocks ("Progress: {" ... "}"); each completed WORKING block yields the
/// fraction complete and ETA reported by the encoder.
/// </summary>
public class HandBrakeProgressParser
{
    private const string BlockStart = "Progress: {";
    private const string BlockEnd = "}";

    private StringBuilder? _block;

    public EncodeProgress? ProcessLine(string line)
    {
        if (_block is null)
        {
            if (line.StartsWith(BlockStart, StringComparison.Ordinal))
            {
                _block = new StringBuilder("{");
            }

            return null;
        }

        _block.Append(line);

        if (line != BlockEnd)
        {
            return null;
        }

        var json = _block.ToString();
        _block = null;
        return TryParse(json);
    }

    private static EncodeProgress? TryParse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("Working", out var working) &&
                working.TryGetProperty("Progress", out var fraction))
            {
                double? eta = working.TryGetProperty("ETASeconds", out var etaSeconds)
                    ? etaSeconds.GetDouble()
                    : null;

                return new EncodeProgress(fraction.GetDouble(), eta);
            }
        }
        catch (JsonException)
        {
            // A malformed block just means no progress update; keep streaming.
        }

        return null;
    }
}
