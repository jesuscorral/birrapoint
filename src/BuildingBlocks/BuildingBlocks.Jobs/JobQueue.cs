using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Jobs;

internal sealed class JobQueue<TContext>(TContext db, IJobWakeUp wakeUp) : IJobQueue
    where TContext : DbContext
{
    public async Task EnqueueAsync(
        Guid groupKey, string type, object payload, CancellationToken cancellationToken = default)
    {
        Stage(groupKey, type, payload);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Best-effort wake-up after commit; the worker's safety-net poll still picks this job up
        // even if this signal is somehow missed.
        wakeUp.Signal();
    }

    public void Stage(Guid groupKey, string type, object payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(payload);

        var now = DateTimeOffset.UtcNow;
        db.Set<Job>().Add(new Job
        {
            GroupKey = groupKey,
            Type = type,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAt = now,
            UpdatedAt = now,
        });
    }
}
