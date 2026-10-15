using MediaTryk.Encoding.HandBrake;

namespace MediaTryk.Media;

/// <summary>
/// In-memory list of every encoded file in the media tree, served by
/// GET /api/media/encoded. Walking the tree is slow on Unraid user shares, so
/// the list is built once, patched when an encode completes, and rebuilt only
/// when it is older than <see cref="MaxAge"/> or a client asks for a refresh
/// (which picks up files added or removed outside MediaTryk).
/// </summary>
public class EncodedMediaCatalog(MediaPathResolver resolver, ILogger<EncodedMediaCatalog> logger)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(15);

    private readonly object _gate = new();
    private Dictionary<string, EncodedMediaDto>? _entries;
    private DateTimeOffset _builtAt;
    private Task<Dictionary<string, EncodedMediaDto>>? _scan;

    /// <summary>
    /// The encoded files sorted by media path. Concurrent callers share one
    /// scan when a rebuild is needed.
    /// </summary>
    public async Task<IReadOnlyList<EncodedMediaDto>> GetAllAsync(bool refresh = false)
    {
        Task<Dictionary<string, EncodedMediaDto>> scan;
        lock (_gate)
        {
            if (!refresh && _entries is not null && DateTimeOffset.UtcNow - _builtAt < MaxAge)
            {
                return Sorted(_entries);
            }

            _scan ??= Task.Run(Scan);
            scan = _scan;
        }

        var built = await scan;
        lock (_gate)
        {
            if (ReferenceEquals(_scan, scan))
            {
                _entries = built;
                _builtAt = DateTimeOffset.UtcNow;
                _scan = null;
            }

            return Sorted(_entries ?? built);
        }
    }

    /// <summary>
    /// Records (or refreshes) one encoded file, e.g. right after an encode
    /// moved it into place. A no-op until the first full scan has run.
    /// </summary>
    public void Upsert(string mediaPath)
    {
        try
        {
            if (!resolver.TryResolveMedia(mediaPath, out var full) || !File.Exists(full))
            {
                return;
            }

            var file = new FileInfo(full);
            var entry = new EncodedMediaDto(
                FindSourcePath(mediaPath, SourceNamesIn(Path.GetDirectoryName(mediaPath) ?? string.Empty)),
                mediaPath,
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));

            lock (_gate)
            {
                _entries?.Remove(mediaPath);
                _entries?.Add(mediaPath, entry);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not update the encoded list for {MediaPath}", mediaPath);
        }
    }

    private static List<EncodedMediaDto> Sorted(Dictionary<string, EncodedMediaDto> entries) =>
        entries.Values.OrderBy(e => e.MediaPath, StringComparer.Ordinal).ToList();

    private Dictionary<string, EncodedMediaDto> Scan()
    {
        var mediaRoot = resolver.MediaRootPath;
        var results = new Dictionary<string, EncodedMediaDto>(StringComparer.Ordinal);
        if (!Directory.Exists(mediaRoot))
        {
            return results;
        }

        var pending = new Stack<string>();
        pending.Push(mediaRoot);

        while (pending.Count > 0)
        {
            var dir = new DirectoryInfo(pending.Pop());
            foreach (var sub in dir.GetDirectories())
            {
                // Internal state (queue.json) lives under <media root>/.mediatryk.
                if (dir.FullName == mediaRoot && sub.Name == ".mediatryk")
                {
                    continue;
                }

                pending.Push(sub.FullName);
            }

            HashSet<string>? sourceNames = null;
            foreach (var file in dir.GetFiles())
            {
                // In-progress encodes are "<name>.mp4.encoding", so the extension check skips them.
                if (!string.Equals(file.Extension, HandBrakeEncodeProfile.OutputExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var mediaPath = Path.GetRelativePath(mediaRoot, file.FullName);
                // One listing of the matching source folder per media folder,
                // instead of an existence check per file.
                sourceNames ??= SourceNamesIn(Path.GetRelativePath(mediaRoot, dir.FullName));
                results[mediaPath] = new EncodedMediaDto(
                    FindSourcePath(mediaPath, sourceNames),
                    mediaPath,
                    file.Length,
                    new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
            }
        }

        return results;
    }

    /// <summary>File names in the source folder at the same relative path; empty when it doesn't exist.</summary>
    private HashSet<string> SourceNamesIn(string relativeDirectory)
    {
        var dir = relativeDirectory == "." ? string.Empty : relativeDirectory;
        if (!resolver.TryResolveSource(dir, out var full) || !Directory.Exists(full))
        {
            return [];
        }

        return new DirectoryInfo(full).GetFiles().Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Same relative stem in the source tree; .mkv preferred over .mp4.</summary>
    private static string? FindSourcePath(string mediaPath, HashSet<string> sourceNames)
    {
        foreach (var ext in new[] { ".mkv", ".mp4" })
        {
            var candidate = Path.ChangeExtension(mediaPath, ext);
            if (sourceNames.Contains(Path.GetFileName(candidate)))
            {
                return candidate;
            }
        }

        return null;
    }
}
