using BuildingBlocks.Jobs;

namespace BirraPoint.Api.Realtime;

/// <summary>`{ jobType, status, detail? }` per contracts/signalr-hub.md's `DispatchProgress` event.</summary>
internal sealed record DispatchProgressPayload(string JobType, JobStatus Status, string? Detail);

/// <summary>Publishes the `DispatchProgress` event to the competition's organizers whenever the job queue
/// block persists a state change (R-06). Fire-and-forget: the block isolates any failure from here.</summary>
internal sealed class DispatchProgressObserver(IEventPublisher eventPublisher) : IJobObserver
{
    public Task OnStateChangedAsync(Job job, string? detail, CancellationToken cancellationToken) =>
        eventPublisher.PublishToOrganizersAsync(
            job.GroupKey, CompetitionEvents.DispatchProgress,
            new DispatchProgressPayload(job.Type, job.Status, detail), cancellationToken);
}
