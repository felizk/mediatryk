using System.Globalization;

namespace MediaTryk.Encoding.HandBrake;

/// <summary>
/// Builds the full HandBrakeCLI argument list for encoding a source file: the
/// audio/subtitle tracks resolved by TrackSelector, plus the fixed
/// HandBrakeEncodeProfile settings. Only the first selected subtitle is used,
/// since it's burned into the video rather than kept as a soft track.
/// </summary>
public static class HandBrakeArgumentBuilder
{
    public static IReadOnlyList<string> Build(
        string sourceFullPath,
        string destinationFullPath,
        HandBrakeTrackSelection selection)
    {
        var args = new List<string>
        {
            "-i", sourceFullPath,
            "-o", destinationFullPath,

            "-f", HandBrakeEncodeProfile.ContainerFormat,
            "-O",

            "-e", HandBrakeEncodeProfile.VideoEncoder,
            "--encoder-preset", HandBrakeEncodeProfile.EncoderPreset,
            "-q", HandBrakeEncodeProfile.VideoQuality.ToString(CultureInfo.InvariantCulture),
            "-X", HandBrakeEncodeProfile.MaxWidth.ToString(CultureInfo.InvariantCulture),
            "-Y", HandBrakeEncodeProfile.MaxHeight.ToString(CultureInfo.InvariantCulture),

            "-a", selection.AudioTrackIndices.Count > 0
                ? string.Join(',', selection.AudioTrackIndices)
                : "none",
            "-E", HandBrakeEncodeProfile.AudioEncoder,
            "-6", HandBrakeEncodeProfile.AudioMixdown,
            "-B", HandBrakeEncodeProfile.AudioBitrateKbps.ToString(CultureInfo.InvariantCulture)
        };

        if (selection.SubtitleTrackIndices.Count > 0)
        {
            args.Add("-s");
            args.Add(selection.SubtitleTrackIndices[0].ToString(CultureInfo.InvariantCulture));
            args.Add("--subtitle-burned=1");
        }

        return args;
    }
}
