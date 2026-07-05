namespace MediaTryk.Encoding.HandBrake;

/// <summary>
/// Fixed encode settings applied to every job: 10-bit x265 at 720p, stereo AAC
/// audio, mp4 output optimized for web streaming, with the selected subtitle
/// burned into the video.
/// </summary>
public static class HandBrakeEncodeProfile
{
    public const string VideoEncoder = "x265_10bit";
    public const string EncoderPreset = "fast";

    // Intel QSV (VAAPI-backed) equivalents, used when HandBrakeCapabilities
    // reports the hardware as usable. QSV presets are speed/balanced/quality;
    // the ICQ value runs a bit higher than x265's CRF since hardware HEVC is
    // less efficient per bit at the same number.
    public const string HardwareVideoEncoder = "qsv_h265_10bit";
    public const string HardwareEncoderPreset = "quality";
    public const double HardwareVideoQuality = 22;

    public const double VideoQuality = 20;

    public const int MaxWidth = 1280;
    public const int MaxHeight = 720;

    public const string AudioEncoder = "av_aac";
    public const string AudioMixdown = "stereo";
    public const int AudioBitrateKbps = 192;

    public const string ContainerFormat = "av_mp4";
    public const string OutputExtension = ".mp4";
}
