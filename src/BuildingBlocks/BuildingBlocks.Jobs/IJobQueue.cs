namespace BuildingBlocks.Jobs;

/// <summary>
/// Creates <see cref="Job"/> rows. Two modes: <see cref="EnqueueAsync"/> saves on its own, so call it after
/// your own business-transaction commit; <see cref="Stage"/> only adds the job to the change tracker so it
/// commits atomically with the caller's own <c>SaveChangesAsync</c> — the caller then signals
/// <see cref="IJobWakeUp"/> after that commit.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "A database-backed job queue, not a System.Collections.Queue; the name is the domain term.")]
public interface IJobQueue
{
    /// <summary>Stages the job, saves the host context and wakes the worker.</summary>
    Task EnqueueAsync(Guid groupKey, string type, object payload, CancellationToken cancellationToken = default);

    /// <summary>Adds the job to the change tracker without saving; it commits with the caller's unit of work.</summary>
    void Stage(Guid groupKey, string type, object payload);
}
