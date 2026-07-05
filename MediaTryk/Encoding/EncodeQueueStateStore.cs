using System.Text.Json;
using System.Text.Json.Serialization;
using MediaTryk.Media;

namespace MediaTryk.Encoding;

/// <summary>
/// Persists the encode queue as JSON in a hidden folder under the media root,
/// so the queue survives container restarts without needing an extra volume.
/// Writes go to a temp file first and are moved into place, so a crash
/// mid-write can't corrupt the previous state.
/// </summary>
public class EncodeQueueStateStore(MediaPathResolver resolver, ILogger<EncodeQueueStateStore> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _stateFilePath =
        Path.Combine(resolver.MediaRootPath, ".mediatryk", "queue.json");

    private readonly object _writeLock = new();

    public IReadOnlyList<EncodeJob> Load()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return [];
            }

            var json = File.ReadAllText(_stateFilePath);
            return JsonSerializer.Deserialize<List<EncodeJob>>(json, SerializerOptions) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not load persisted encode queue from {Path}; starting with an empty queue.",
                _stateFilePath);
            return [];
        }
    }

    public void Save(IReadOnlyCollection<EncodeJob> jobs)
    {
        try
        {
            lock (_writeLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);

                var tempPath = _stateFilePath + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(jobs, SerializerOptions));
                File.Move(tempPath, _stateFilePath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not persist encode queue to {Path}.", _stateFilePath);
        }
    }
}
