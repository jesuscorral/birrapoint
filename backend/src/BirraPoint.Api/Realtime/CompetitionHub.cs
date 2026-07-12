using BirraPoint.Api.Common.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Realtime;

/// <summary>
/// Server → client only (contracts/signalr-hub.md) — clients invoke only the group-join methods
/// below; every mutation still goes through the REST API (Principle VI). `[Authorize]` rejects
/// unauthenticated connections via the T011 deny-by-default fallback policy.
///
/// Tracked gap (T015 plan, confirmed with the user before implementation): the DB-backed checks
/// below (competition ownership, table membership) have no integration/contract test yet — T018
/// (the shared WebApplicationFactory + Testcontainers harness) lands after this task, and the
/// constitution bans EF Core's InMemory provider as a substitute. Add that coverage once T018's
/// harness exists; `contract-guardian` can verify this gap is closed.
/// </summary>
[Authorize]
public sealed class CompetitionHub(AppDbContext db) : Hub
{
    public async Task JoinCompetitionAsOrganizer(Guid competitionId)
    {
        var user = Context.User ?? throw new HubException("Not authorized to join this competition's organizer group.");
        if (!user.IsInRole("ORGANIZER"))
        {
            throw new HubException("Not authorized to join this competition's organizer group.");
        }

        var sub = user.FindFirst("sub")?.Value
            ?? throw new HubException("Not authorized to join this competition's organizer group.");

        var owns = await db.Competitions.AnyAsync(competition =>
            competition.Id == competitionId && competition.CreatedByUserId == sub);
        if (!owns)
        {
            throw new HubException("Not authorized to join this competition's organizer group.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, CompetitionGroups.Organizers(competitionId));
    }

    public async Task JoinTable(Guid tableId)
    {
        var user = Context.User ?? throw new HubException("Not authorized to join this table.");
        var sub = user.FindFirst("sub")?.Value;
        var email = user.FindFirst("email")?.Value;

        // KeycloakUserId is only backfilled once JudgeResolver (T023, US1) runs on a REST call; a
        // judge whose first authenticated action is opening this socket falls back to email match.
        var isActiveMember = await db.TableJudges
            .Where(tableJudge => tableJudge.TastingTableId == tableId && tableJudge.RemovedAt == null)
            .Join(db.Judges, tableJudge => tableJudge.JudgeId, judge => judge.Id, (_, judge) => judge)
            .AnyAsync(judge => judge.KeycloakUserId == sub || (judge.KeycloakUserId == null && judge.Email == email));
        if (!isActiveMember)
        {
            throw new HubException("Not authorized to join this table.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, CompetitionGroups.Table(tableId));
    }

    public Task LeaveTable(Guid tableId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, CompetitionGroups.Table(tableId));

    public Task LeaveCompetition(Guid competitionId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, CompetitionGroups.Organizers(competitionId));
}
