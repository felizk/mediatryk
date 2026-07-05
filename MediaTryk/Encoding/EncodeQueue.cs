using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
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

public enum EncodeRequeueResult
{
    NotFound,
    Requeued,
    NotRequeueable
}

/// <summary>
/// Holds queued encode jobs and hands them out one at a time to the background worker,
/// while also tracking every job's status for lookup via the API. The job list is
/// persisted via EncodeQueueStateStore; on startup, jobs that were pending when the
/// server stopped are re-enqueued as Queued (progress starts over).
///
/// Processing order is driven entirely by EncodeJob.Order: the worker's ReadAllAsync
/// always yields the queued job with the lowest Order, and the channel is just a
/// wake-up signal (one write per job that becomes Queued). This is what lets Requeue
/// slot a job ahead of everything already waiting. A signal whose job was canceled
/// while still queued is absorbed by a no-op loop iteration, so counts stay balanced
/// and no queued job is ever left without a wake-up.
/// </summary>
public class EncodeQueue
{
    private readonly Channel<bool> _signals = Channel.CreateUnbounded<bool>();
    private readonly ConcurrentDictionary<Guid, EncodeJob> _jobs = new();
    private readonly ConcurrentDictionary<Guid, Channel<EncodeJob>> _subscribers = new();
    private readonly EncodeQueueStateStore _store;
    private long _nextOrder;

    public EncodeQueue(EncodeQueueStateStore store)
    {
        _store = store;

        // Renumber on load (preserving persisted order) so the counter restarts
        // compactly and pre-Order state files (every job at 0) fall back to
        // QueuedAt order.
        foreach (var job in store.Load().OrderBy(j => j.Order).ThenBy(j => j.QueuedAt))
        {
            job.Order = _nextOrder++;

            if (job.Status is EncodeJobStatus.Queued or EncodeJobStatus.Running)
            {
                job.Status = EncodeJobStatus.Queued;
                job.Progress = null;
                job.EtaSeconds = null;
                job.StartedAt = null;
                _signals.Writer.TryWrite(true);
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
            QueuedAt = DateTimeOffset.UtcNow,
            Order = Interlocked.Increment(ref _nextOrder)
        };

        _jobs[job.Id] = job;
        _signals.Writer.TryWrite(true);
        NotifyChanged(job);
        PersistState();
        return job;
    }

    // A requeued job shares its Order with the job that was running when it was
    // requeued (min(queued) - 1 lands on it); StartedAt breaks that tie so the
    // running/finished job sorts first — "requeued to run right after this one".
    public IReadOnlyCollection<EncodeJob> GetAll() =>
        _jobs.Values
            .OrderBy(j => j.Order)
            .ThenBy(j => j.StartedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(j => j.QueuedAt)
            .ToList();

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

    /// <summary>
    /// Re-enqueues a failed or canceled job under its existing id (so subscribers see
    /// an in-place status change rather than a new entry), moving it to the front of
    /// the queue: it runs right after the current encode, or immediately if the worker
    /// is idle. Jobs in any other state are left untouched.
    /// </summary>
    public EncodeRequeueResult Requeue(Guid id, out EncodeJob? job)
    {
        if (!_jobs.TryGetValue(id, out job))
        {
            return EncodeRequeueResult.NotFound;
        }

        // The job object is the lock guarding its status transitions,
        // shared with Cancel and the worker.
        lock (job)
        {
            if (job.Status is not (EncodeJobStatus.Failed or EncodeJobStatus.Canceled))
            {
                return EncodeRequeueResult.NotRequeueable;
            }

            // Below every queued job's Order = front of the queue. (This job is
            // still Failed/Canceled here, so it doesn't shadow the minimum.)
            var frontOrder = _jobs.Values
                .Where(j => j.Status == EncodeJobStatus.Queued)
                .Select(j => (long?)j.Order)
                .Min();

            job.Order = frontOrder is { } front ? front - 1 : Interlocked.Increment(ref _nextOrder);
            job.Status = EncodeJobStatus.Queued;
            job.Progress = null;
            job.EtaSeconds = null;
            job.StartedAt = null;
            job.CompletedAt = null;
            job.ErrorMessage = null;
        }

        _signals.Writer.TryWrite(true);
        NotifyChanged(job);
        PersistState();
        return EncodeRequeueResult.Requeued;
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

    /// <summary>
    /// Yields jobs to the worker in processing order. Each wake-up signal selects the
    /// lowest-Order queued job at that moment — not necessarily the job whose enqueue
    /// wrote the signal, which is how requeued jobs jump the line.
    /// </summary>
    public async IAsyncEnumerable<EncodeJob> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var _ in _signals.Reader.ReadAllAsync(cancellationToken))
        {
            var job = _jobs.Values
                .Where(j => j.Status == EncodeJobStatus.Queued)
                .OrderBy(j => j.Order)
                .ThenBy(j => j.QueuedAt)
                .FirstOrDefault();

            // No queued job means this signal's job was canceled while
            // waiting; absorb the signal and keep listening.
            if (job is not null)
            {
                yield return job;
            }
        }
    }

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
