using BirraPoint.Api.Modules.Judging.Contracts.IntegrationEvents;
using BirraPoint.Api.Modules.Organization.Contracts;
using BuildingBlocks.Messaging;

namespace BirraPoint.Api.Modules.Organization.Integration;

/// <summary>
/// FR-018, competition-wide: when a table's judge set changes, recomputes the flag of every entry owned by an
/// added or removed judge against current engagement. Order-insensitive and idempotent (FR-079): a late or
/// repeated event converges to the same flags.
/// </summary>
internal sealed class UpdateBosFlagsOnTableJudgesChanged(
    IConflictOfInterestChecker conflictChecker, BosFlagRecalculator recalculator)
    : IIntegrationEventHandler<TableJudgesChanged>
{
    public async Task HandleAsync(
        TableJudgesChanged integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        var judgeIds = integrationEvent.AddedJudgeIds.Concat(integrationEvent.RemovedJudgeIds).Distinct().ToList();
        if (judgeIds.Count == 0)
        {
            return;
        }

        var owned = await conflictChecker.GetEntriesOwnedByJudgesAsync(
            integrationEvent.CompetitionId, judgeIds, cancellationToken);
        var entryIds = owned.Values.SelectMany(entries => entries).ToHashSet();

        await recalculator.RecalculateAsync(integrationEvent.CompetitionId, entryIds, cancellationToken);
    }
}
