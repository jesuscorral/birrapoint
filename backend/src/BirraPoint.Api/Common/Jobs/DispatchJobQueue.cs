using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Common.Jobs;

/// <summary>
/// Creates <see cref="DispatchJob"/> rows (R-06). Two modes: <see cref="EnqueueAsync"/> saves on its
/// own, so call it after your own business-transaction commit (same convention as
/// <see cref="Realtime.IEventPublisher"/>); <see cref="Stage"/> only adds the job to the change tracker
/// so it commits atomically with the caller's own <c>SaveChangesAsync</c> — the caller then signals
/// <see cref="IDispatchWakeUp"/> after that commit.
/// </summary>
internal interface IDispatchJobQueue
{
    Task EnqueueAsync(
        Guid competitionId, DispatchJobType type, object payload, CancellationToken cancellationToken = default);

    /// <summary>Adds the job to the change tracker without saving; it commits with the caller's unit of work.</summary>
    void Stage(Guid competitionId, DispatchJobType type, object payload);
}

internal sealed class DispatchJobQueue(AppDbContext db, IDispatchWakeUp wakeUp) : IDispatchJobQueue
{
    public async Task EnqueueAsync(
        Guid competitionId, DispatchJobType type, object payload, CancellationToken cancellationToken = default)
    {
        Stage(competitionId, type, payload);
        await db.SaveChangesAsync(cancellationToken);

        // Best-effort wake-up after commit; the worker's safety-net poll still picks this job up
        // even if this signal is somehow missed.
        wakeUp.Signal();
    }

    public void Stage(Guid competitionId, DispatchJobType type, object payload) =>
        db.DispatchJobs.Add(new DispatchJob
        {
            CompetitionId = competitionId,
            Type = type,
            PayloadJson = JsonSerializer.Serialize(payload),
        });
}
