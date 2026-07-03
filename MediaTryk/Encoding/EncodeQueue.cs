using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MediaTryk.Encoding;

/// <summary>
/// Holds queued encode jobs and hands them out one at a time to the background worker,
/// while also tracking every job's status for lookup via the API.
/// </summary>
public class EncodeQueue
{
    private readonly Channel<EncodeJob> _channel = Channel.CreateUnbounded<EncodeJob>();
    private readonly ConcurrentDictionary<Guid, EncodeJob> _jobs = new();

    public EncodeJob Enqueue(string sourcePath)
    {
        var job = new EncodeJob
        {
            Id = Guid.NewGuid(),
            SourcePath = sourcePath,
            QueuedAt = DateTimeOffset.UtcNow
        };

        _jobs[job.Id] = job;
        _channel.Writer.TryWrite(job);
        return job;
    }

    public IReadOnlyCollection<EncodeJob> GetAll() =>
        _jobs.Values.OrderBy(j => j.QueuedAt).ToList();

    public bool TryGet(Guid id, out EncodeJob? job) => _jobs.TryGetValue(id, out job);

    public IAsyncEnumerable<EncodeJob> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
