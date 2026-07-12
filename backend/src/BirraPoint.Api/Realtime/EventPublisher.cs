using Microsoft.AspNetCore.SignalR;

namespace BirraPoint.Api.Realtime;

/// <summary>
/// Emit-after-commit dispatcher (T015). Generic on purpose: it knows how to reach the two fixed
/// audiences, not the payload shape of any specific event — each story's handler builds its own
/// payload record and calls this after its own <c>SaveChangesAsync</c> succeeds, never before
/// (contracts/signalr-hub.md §Delivery semantics — no phantom updates from rolled-back transactions).
/// </summary>
public interface IEventPublisher
{
    Task PublishToOrganizersAsync(
        Guid competitionId, string eventName, object payload, CancellationToken cancellationToken = default);

    Task PublishToTableAsync(
        Guid tableId, string eventName, object payload, CancellationToken cancellationToken = default);
}

public sealed class EventPublisher(IHubContext<CompetitionHub> hubContext) : IEventPublisher
{
    public Task PublishToOrganizersAsync(
        Guid competitionId, string eventName, object payload, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(CompetitionGroups.Organizers(competitionId)).SendAsync(eventName, payload, cancellationToken);

    public Task PublishToTableAsync(
        Guid tableId, string eventName, object payload, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(CompetitionGroups.Table(tableId)).SendAsync(eventName, payload, cancellationToken);
}
