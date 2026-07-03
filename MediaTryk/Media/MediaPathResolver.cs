using Microsoft.Extensions.Options;

namespace MediaTryk.Media;

/// <summary>
/// Resolves user-supplied relative paths against the configured media root,
/// rejecting anything that escapes the root (e.g. via "..").
/// </summary>
public class MediaPathResolver(IOptions<MediaLibraryOptions> options)
{
    private readonly string _root = Path.GetFullPath(options.Value.RootPath);

    public string RootPath => _root;

    public bool TryResolve(string? relativePath, out string fullPath)
    {
        var combined = Path.GetFullPath(Path.Combine(_root, relativePath ?? string.Empty));
        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;

        if (combined != _root && !combined.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            fullPath = string.Empty;
            return false;
        }

        fullPath = combined;
        return true;
    }
}
