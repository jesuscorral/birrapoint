using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Common.Jobs;

/// <summary>
/// Enqueues a <see cref="DispatchJob"/> (T016/R-06). Call after your own business-transaction
/// commit, same convention as <see cref="Realtime.IEventPublisher"/> — enqueuing is itself a
/// side effect that must not survive a rolled-back transaction.
/// </summary>
public interface IDispatchJobQueue
{
    Task EnqueueAsync(
        Guid competitionId, DispatchJobType type, object payload, CancellationToken cancellationToken = default);
}

public sealed class DispatchJobQueue(AppDbContext db, IDispatchWakeUp wakeUp) : IDispatchJobQueue
{
    public async Task EnqueueAsync(
        Guid competitionId, DispatchJobType type, object payload, CancellationToken cancellationToken = default)
    {
        var job = new DispatchJob
        {
            CompetitionId = competitionId,
            Type = type,
            PayloadJson = JsonSerializer.Serialize(payload),
        };

        db.DispatchJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        // Best-effort wake-up after commit; the worker's safety-net poll still picks this job up
        // even if this signal is somehow missed.
        wakeUp.Signal();
    }
}
