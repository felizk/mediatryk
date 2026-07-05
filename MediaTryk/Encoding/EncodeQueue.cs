using System.Collections.Concurrent;
using System.Threading.Channels;
using MediaTryk.Encoding.HandBrake;

namespace MediaTryk.Encoding;

/// <summary>
/// Holds queued encode jobs and hands them out one at a time to the background worker,
/// while also tracking every job's status for lookup via the API.
/// </summary>
public class EncodeQueue
{
    private readonly Channel<EncodeJob> _channel = Channel.CreateUnbounded<EncodeJob>();
    private readonly ConcurrentDictionary<Guid, EncodeJob> _jobs = new();
    private readonly ConcurrentDictionary<Guid, Channel<EncodeJob>> _subscribers = new();

    public EncodeJob Enqueue(string sourcePath)
    {
        var job = new EncodeJob
        {
            Id = Guid.NewGuid(),
            SourcePath = sourcePath,
            DestinationPath = Path.ChangeExtension(sourcePath, HandBrakeEncodeProfile.OutputExtension),
            QueuedAt = DateTimeOffset.UtcNow
        };

        _jobs[job.Id] = job;
        _channel.Writer.TryWrite(job);
        NotifyChanged(job);
        return job;
    }

    public IReadOnlyCollection<EncodeJob> GetAll() =>
        _jobs.Values.OrderBy(j => j.QueuedAt).ToList();

    public bool TryGet(Guid id, out EncodeJob? job) => _jobs.TryGetValue(id, out job);

    public bool IsActive(string sourcePath) =>
        _jobs.Values.Any(j =>
            j.Status is EncodeJobStatus.Queued or EncodeJobStatus.Running &&
            string.Equals(j.SourcePath, sourcePath, StringComparison.Ordinal));

    public IAsyncEnumerable<EncodeJob> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public (Guid Id, ChannelReader<EncodeJob> Reader) Subscribe()
    {
        var subscription = Channel.CreateUnbounded<EncodeJob>();
        var id = Guid.NewGuid();
        _subscribers[id] = subscription;
        return (id, subscription.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var subscription))
        {
            subscription.Writer.TryComplete();
        }
    }

    public void NotifyChanged(EncodeJob job)
    {
        foreach (var subscription in _subscribers.Values)
        {
            subscription.Writer.TryWrite(job);
        }
    }
}
