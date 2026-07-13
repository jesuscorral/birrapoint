using Microsoft.AspNetCore.SignalR;

namespace BirraPoint.Api.Realtime;

/// <summary>
/// Emit-after-commit dispatcher (T015). Generic on purpose: it knows how to reach the two fixed
/// audiences, not the payload shape of any specific event — each story's handler builds its own
/// payload record and calls this after its own <c>SaveChangesAsync</c> succeeds, never before
/// (contracts/signalr-hub.md §Delivery semantics — no phantom updates from rolled-back transactions).
/// Callers should pass <see cref="CancellationToken.None"/> (the default) rather than the
/// originating request's token: this call happens after commit, so cancelling it on client
/// disconnect would silently drop a notification the contract otherwise treats as fire-and-forget
/// but expected to fire once the transaction succeeded.
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
