using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Integration;

/// <summary>
/// Recomputes <c>NotValidForBos</c> (FR-018) for the given entries of one competition from current state: set
/// when some co-owner judge is engaged (seated anywhere, or has submitted an evaluation). Stages the changes
/// on the shared DbContext; the caller owns the unit of work. Reads persisted judges and seats, so a caller that
/// just changed them must have saved first.
/// </summary>
internal sealed class BosFlagRecalculator(
    AppDbContext dbContext, IConflictOfInterestChecker conflictChecker, IJudgeEngagement judgeEngagement)
{
    public async Task RecalculateAsync(
        Guid competitionId, IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            return;
        }

        var coOwnersByEntryId = await conflictChecker.GetCoOwnerJudgesAsync(competitionId, entryIds, cancellationToken);
        var engaged = await judgeEngagement.GetEngagedJudgesAsync(
            competitionId, coOwnersByEntryId.Values.SelectMany(judgeIds => judgeIds).ToHashSet(), cancellationToken);
        var flags = BosFlagRules.Compute(entryIds, coOwnersByEntryId, engaged);

        var ids = flags.Keys.ToList();
        var entries = await dbContext.BeerEntries
            .Where(e => e.CompetitionId == competitionId && ids.Contains(e.Id))
            .ToListAsync(cancellationToken);
        foreach (var entry in entries)
        {
            entry.NotValidForBos = flags[entry.Id];
        }
    }
}
