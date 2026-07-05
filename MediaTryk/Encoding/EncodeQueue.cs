using System.Collections.Concurrent;
using System.Threading.Channels;
using MediaTryk.Encoding.HandBrake;

namespace MediaTryk.Encoding;

public enum EncodeCancelResult
{
    NotFound,
    Canceled,
    CancellationRequested,
    AlreadyFinished
}

/// <summary>
/// Holds queued encode jobs and hands them out one at a time to the background worker,
/// while also tracking every job's status for lookup via the API. The job list is
/// persisted via EncodeQueueStateStore; on startup, jobs that were pending when the
/// server stopped are re-enqueued as Queued (progress starts over).
/// </summary>
public class EncodeQueue
{
    private readonly Channel<EncodeJob> _channel = Channel.CreateUnbounded<EncodeJob>();
    private readonly ConcurrentDictionary<Guid, EncodeJob> _jobs = new();
    private readonly ConcurrentDictionary<Guid, Channel<EncodeJob>> _subscribers = new();
    private readonly EncodeQueueStateStore _store;

    public EncodeQueue(EncodeQueueStateStore store)
    {
        _store = store;

        foreach (var job in store.Load().OrderBy(j => j.QueuedAt))
        {
            if (job.Status is EncodeJobStatus.Queued or EncodeJobStatus.Running)
            {
                job.Status = EncodeJobStatus.Queued;
                job.Progress = null;
                job.EtaSeconds = null;
                job.StartedAt = null;
                _channel.Writer.TryWrite(job);
            }

            _jobs[job.Id] = job;
        }
    }

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
        PersistState();
        return job;
    }

    public IReadOnlyCollection<EncodeJob> GetAll() =>
        _jobs.Values.OrderBy(j => j.QueuedAt).ToList();

    public bool TryGet(Guid id, out EncodeJob? job) => _jobs.TryGetValue(id, out job);

    /// <summary>
    /// Cancels a queued job or requests cancellation of a running one;
    /// already-finished jobs are left untouched.
    /// </summary>
    public EncodeCancelResult Cancel(Guid id, out EncodeJob? job)
    {
        if (!_jobs.TryGetValue(id, out job))
        {
            return EncodeCancelResult.NotFound;
        }

        // The job object is the lock guarding its Queued -> Running/Canceled
        // transition, shared with the worker's dequeue check.
        lock (job)
        {
            switch (job.Status)
            {
                case EncodeJobStatus.Queued:
                    job.Status = EncodeJobStatus.Canceled;
                    job.CompletedAt = DateTimeOffset.UtcNow;
                    NotifyChanged(job);
                    PersistState();
                    return EncodeCancelResult.Canceled;

                case EncodeJobStatus.Running:
                    try
                    {
                        job.Cancellation?.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // The job finished at the same moment; nothing to cancel.
                    }
                    return EncodeCancelResult.CancellationRequested;

                default:
                    return EncodeCancelResult.AlreadyFinished;
            }
        }
    }

    public bool IsActive(string sourcePath) =>
        _jobs.Values.Any(j =>
            j.Status is EncodeJobStatus.Queued or EncodeJobStatus.Running &&
            string.Equals(j.SourcePath, sourcePath, StringComparison.Ordinal));

    /// <summary>
    /// Removes all finished jobs (completed, canceled, or failed)
    /// from the list, returning how many were cleared.
    /// </summary>
    public int ClearFinished()
    {
        var removed = 0;

        foreach (var job in _jobs.Values)
        {
            if (job.Status is EncodeJobStatus.Completed or EncodeJobStatus.Canceled or EncodeJobStatus.Failed &&
                _jobs.TryRemove(job.Id, out _))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            PersistState();
        }

        return removed;
    }

    /// <summary>
    /// Writes the current job list to disk. The worker calls this after status
    /// transitions; progress updates are deliberately not persisted (progress
    /// restarts from scratch after a restart anyway).
    /// </summary>
    public void PersistState() => _store.Save(GetAll());

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
