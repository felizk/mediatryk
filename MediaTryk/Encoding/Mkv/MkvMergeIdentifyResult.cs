using System.Text.Json.Serialization;

namespace MediaTryk.Encoding.Mkv;

internal sealed class MkvMergeIdentifyResult
{
    [JsonPropertyName("tracks")]
    public List<MkvMergeTrack> Tracks { get; set; } = [];
}

internal sealed class MkvMergeTrack
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("codec")]
    public string? Codec { get; set; }

    [JsonPropertyName("properties")]
    public MkvMergeTrackProperties Properties { get; set; } = new();
}

internal sealed class MkvMergeTrackProperties
{
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("track_name")]
    public string? TrackName { get; set; }

    [JsonPropertyName("default_track")]
    public bool DefaultTrack { get; set; }

    [JsonPropertyName("forced_track")]
    public bool ForcedTrack { get; set; }

    [JsonPropertyName("enabled_track")]
    public bool EnabledTrack { get; set; } = true;
}
