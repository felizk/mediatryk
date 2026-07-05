namespace MediaTryk.Encoding.HandBrake;

public class HandBrakeOptions
{
    public const string SectionName = "HandBrake";

    /// <summary>
    /// When true (the default), encodes use Intel QSV hardware encoding if
    /// HandBrakeCLI reports it as available; otherwise software x265 is used.
    /// Set to false to force software encoding.
    /// </summary>
    public bool EnableHardwareEncoding { get; set; } = true;
}
