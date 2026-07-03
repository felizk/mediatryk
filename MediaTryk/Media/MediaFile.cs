namespace MediaTryk.Media;

public static class MediaFile
{
    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv",
        ".mp4"
    };

    public static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mkv"] = "video/x-matroska",
        [".mp4"] = "video/mp4"
    };

    public static bool IsAllowed(string fileName) =>
        AllowedExtensions.Contains(Path.GetExtension(fileName));

    public static string GetContentType(string fileName) =>
        ContentTypes.GetValueOrDefault(Path.GetExtension(fileName), "application/octet-stream");
}
