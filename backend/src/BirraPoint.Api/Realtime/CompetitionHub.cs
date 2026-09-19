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

        var owns = await db.Competitions.AnyAsync(competition =>
            competition.Id == competitionId && competition.CreatedByUserId == sub);
        if (!owns)
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

        // KeycloakUserId is only backfilled once JudgeResolver (T023, US1) runs on a REST call; a
        // judge whose first authenticated action is opening this socket falls back to email
        // match. sub is required non-null above so `KeycloakUserId == sub` can never degrade to
        // an `IS NULL` check that would match any not-yet-backfilled judge on the table; email is
        // guarded the same way so a null claim can't do the same on the fallback clause. The
        // realm disables self-registration (infra/keycloak/birrapoint-realm.json,
        // registrationAllowed: false), so judge emails are always admin-provisioned, not
        // attacker-chosen.
        var isActiveMember = await db.TableJudges
            .Where(tableJudge => tableJudge.TastingTableId == tableId && tableJudge.RemovedAt == null)
            .Join(db.Judges, tableJudge => tableJudge.JudgeId, judge => judge.Id, (_, judge) => judge)
            .AnyAsync(judge =>
                judge.KeycloakUserId == sub || (judge.KeycloakUserId == null && email != null && judge.Email == email));
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
