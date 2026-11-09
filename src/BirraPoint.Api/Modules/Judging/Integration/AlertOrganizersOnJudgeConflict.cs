using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Contracts.IntegrationEvents;
using BirraPoint.Api.Realtime;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Integration;

/// <summary>
/// FR-082: after a judge's email changed, tells the competition's organizers about every table where the judge is
/// seated and now owns a sample (FR-017). Nothing is unseated: the organizer fixes the table. Reads current state,
/// so a repeated or late event gives the same answer; the push goes out once the delivery committed (FR-084).
/// </summary>
internal sealed class AlertOrganizersOnJudgeConflict(
    AppDbContext dbContext, IConflictOfInterestChecker conflictChecker, IJudgeDirectory judgeDirectory, IEventPublisher eventPublisher)
    : IIntegrationEventHandler<JudgeEmailChanged>
{
    public async Task HandleAsync(
        JudgeEmailChanged integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        var competitionId = integrationEvent.CompetitionId;
        var judgeId = integrationEvent.JudgeId;

        var ownedByJudge = await conflictChecker.GetEntriesOwnedByJudgesAsync(competitionId, [judgeId], cancellationToken);
        if (!ownedByJudge.TryGetValue(judgeId, out var ownedEntryIds) || ownedEntryIds.Count == 0)
        {
            return;
        }

        var ownedIds = ownedEntryIds.ToList();
        var conflicts = await dbContext.TableJudges
            .AsNoTracking()
            .Where(tj => tj.JudgeId == judgeId && tj.RemovedAt == null)
            .Join(dbContext.TastingTables.Where(t => t.CompetitionId == competitionId), tj => tj.TastingTableId, t => t.Id, (tj, t) => t.Id)
            .Join(dbContext.TableSamples, tableId => tableId, ts => ts.TastingTableId, (tableId, ts) => new { TableId = tableId, ts.BeerEntryId })
            .Where(x => ownedIds.Contains(x.BeerEntryId))
            .ToListAsync(cancellationToken);
        if (conflicts.Count == 0)
        {
            return;
        }

        var displayNames = await judgeDirectory.GetDisplayNamesAsync([judgeId], cancellationToken);
        if (!displayNames.TryGetValue(judgeId, out var judgeDisplayName))
        {
            return;
        }

        var alerts = conflicts
            .GroupBy(x => x.TableId)
            .Select(g => new
            {
                tableId = g.Key,
                judgeId,
                judgeDisplayName,
                beerEntryIds = g.Select(x => x.BeerEntryId).Distinct().ToList(),
            })
            .ToList();

        context.OnCommitted(async _ =>
        {
            foreach (var alert in alerts)
            {
                await eventPublisher.PublishToOrganizersAsync(competitionId, CompetitionEvents.JudgeConflictDetected, alert, CancellationToken.None);
            }
        });
    }
}
