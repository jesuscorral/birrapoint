using BirraPoint.Common.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace BirraPoint.Api.Realtime;

/// <summary>Sends events to the SignalR groups of <see cref="CompetitionHub"/>; see <see cref="IEventPublisher"/>.</summary>
internal sealed class EventPublisher(IHubContext<CompetitionHub> hubContext) : IEventPublisher
{
    public Task PublishToOrganizersAsync(
        Guid competitionId, string eventName, object payload, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(CompetitionGroups.Organizers(competitionId)).SendAsync(eventName, payload, cancellationToken);

    public Task PublishToTableAsync(
        Guid tableId, string eventName, object payload, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(CompetitionGroups.Table(tableId)).SendAsync(eventName, payload, cancellationToken);
}
