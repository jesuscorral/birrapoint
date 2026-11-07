namespace BuildingBlocks.Jobs;

/// <summary>
/// Optional hook told about every persisted state change of a job (completed, failed attempt, retry
/// scheduled, lease recovered). Fire-and-forget: it is not the source of truth, so the worker logs and
/// swallows any exception an observer throws and never reverts the job's already-persisted outcome. Observers
/// are resolved from a fresh DI scope per notification; none registered is fine.
/// </summary>
public interface IJobObserver
{
    /// <summary>Called after the new state of <paramref name="job"/> has been persisted.</summary>
    /// <param name="job">The job in its new state.</param>
    /// <param name="detail">The last error for failed attempts; null for completion.</param>
    Task OnStateChangedAsync(Job job, string? detail, CancellationToken cancellationToken);
}
