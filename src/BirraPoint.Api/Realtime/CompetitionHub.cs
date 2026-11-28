using BirraPoint.Api.Modules.Judging.Contracts;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BirraPoint.Api.Realtime;

/// <summary>
/// Server → client only (contracts/signalr-hub.md) — clients invoke only the group-join methods
/// below; every mutation still goes through the REST API (Principle VI). `[Authorize]` rejects
/// unauthenticated connections via the deny-by-default fallback policy.
///
/// Group authorization goes through the owning modules' contracts (competition ownership through
/// <see cref="ICompetitionAccess"/>, table seating through <see cref="ITableMembership"/>), never a data context.
/// </summary>
[Authorize]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles",
    Justification = "Hub method names are the SignalR wire contract (contracts/signalr-hub.md); an Async suffix would break clients.")]
internal sealed class CompetitionHub(ICompetitionAccess competitions, ITableMembership tableMembership) : Hub
{
    private const string NotAuthorizedToJoinOrganizerGroup = "Not authorized to join this competition's organizer group.";
    private const string NotAuthorizedToJoinTable = "Not authorized to join this table.";

    public async Task JoinCompetitionAsOrganizer(Guid competitionId)
    {
        var user = Context.User ?? throw new HubException(NotAuthorizedToJoinOrganizerGroup);
        if (!user.IsInRole("ORGANIZER"))
        {
            throw new HubException(NotAuthorizedToJoinOrganizerGroup);
        }

        var sub = user.FindFirst("sub")?.Value
            ?? throw new HubException(NotAuthorizedToJoinOrganizerGroup);

        var owned = await competitions.GetOwnedAsync(competitionId, sub, Context.ConnectionAborted);
        if (owned is null)
        {
            throw new HubException(NotAuthorizedToJoinOrganizerGroup);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, CompetitionGroups.Organizers(competitionId));
    }

    public async Task JoinTable(Guid tableId)
    {
        var user = Context.User ?? throw new HubException(NotAuthorizedToJoinTable);
        var sub = user.FindFirst("sub")?.Value
            ?? throw new HubException(NotAuthorizedToJoinTable);
        var email = user.FindFirst("email")?.Value;

        // The Keycloak id is only backfilled once a REST call resolves the judge; a judge whose first authenticated
        // action is opening this socket falls back to an e-mail match inside the directory (same rule as before).
        var isActiveMember = await tableMembership.IsActiveMemberAsync(tableId, sub, email, Context.ConnectionAborted);
        if (!isActiveMember)
        {
            throw new HubException(NotAuthorizedToJoinTable);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, CompetitionGroups.Table(tableId));
    }

    public Task LeaveTable(Guid tableId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, CompetitionGroups.Table(tableId));

    public Task LeaveCompetition(Guid competitionId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, CompetitionGroups.Organizers(competitionId));
}
