using MediaTryk.Encoding;
using Microsoft.Extensions.Options;

namespace MediaTryk.Media;

/// <summary>
/// Resolves user-supplied relative paths against the configured media and source roots,
/// rejecting anything that escapes the root (e.g. via "..").
/// </summary>
public class MediaPathResolver(IOptions<MediaLibraryOptions> mediaOptions, IOptions<SourceLibraryOptions> sourceOptions)
{
    public string MediaRootPath { get; } = Path.GetFullPath(mediaOptions.Value.RootPath);

    public string SourceRootPath { get; } = Path.GetFullPath(sourceOptions.Value.RootPath);

    public bool TryResolveMedia(string? relativePath, out string fullPath) =>
        TryResolve(MediaRootPath, relativePath, out fullPath);

    public bool TryResolveSource(string? relativePath, out string fullPath) =>
        TryResolve(SourceRootPath, relativePath, out fullPath);

    private static bool TryResolve(string root, string? relativePath, out string fullPath)
    {
        var combined = Path.GetFullPath(Path.Combine(root, relativePath ?? string.Empty));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (combined != root && !combined.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            fullPath = string.Empty;
            return false;
        }

        fullPath = combined;
        return true;
    }
}
