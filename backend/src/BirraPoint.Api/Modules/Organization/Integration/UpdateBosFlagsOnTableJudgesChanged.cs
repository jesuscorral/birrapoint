using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Contracts.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Integration;

/// <summary>
/// FR-018, competition-wide: an entry is not valid for Best of Show while any judge who owns it (as
/// participant or collaborator) takes part in the tasting. Adding a judge to a table flags every entry
/// they own. Removing one unflags their entries only when no co-owner judge still sits at this table,
/// sits at another table, or has submitted any evaluation. Stages the changes on the shared DbContext;
/// the publishing handler saves them in the same unit of work.
/// </summary>
internal sealed class UpdateBosFlagsOnTableJudgesChanged(
    AppDbContext dbContext, IConflictOfInterestChecker conflictChecker, IJudgeEngagement judgeEngagement)
    : INotificationHandler<TableJudgesChanged>
{
    public async Task Handle(TableJudgesChanged notification, CancellationToken cancellationToken)
    {
        var changedJudgeIds = notification.AddedJudgeIds.Union(notification.RemovedJudgeIds).ToList();
        if (changedJudgeIds.Count == 0)
        {
            return;
        }

        var ownedEntriesByJudgeId = await conflictChecker.GetEntriesOwnedByJudgesAsync(
            notification.CompetitionId, changedJudgeIds, cancellationToken);

        var entriesToFlag = EntriesOwnedBy(notification.AddedJudgeIds, ownedEntriesByJudgeId);
        var unflagCandidates = EntriesOwnedBy(notification.RemovedJudgeIds, ownedEntriesByJudgeId);
        unflagCandidates.ExceptWith(entriesToFlag);

        var entriesToUnflag = await KeepUnflaggableAsync(notification, unflagCandidates, cancellationToken);

        await SetFlagAsync(entriesToFlag, true, cancellationToken);
        await SetFlagAsync(entriesToUnflag, false, cancellationToken);
    }

    /// <summary>The candidates none of whose co-owner judges is still engaged: seated at this table (new
    /// judge set) or engaged elsewhere (another active seat, or any submitted evaluation).</summary>
    private async Task<HashSet<Guid>> KeepUnflaggableAsync(
        TableJudgesChanged notification, HashSet<Guid> candidates, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var coOwnersByEntryId = await conflictChecker.GetCoOwnerJudgesAsync(
            notification.CompetitionId, candidates, cancellationToken);

        var activeAtThisTable = notification.ActiveJudgeIds.ToHashSet();
        var coOwnersOffThisTable = coOwnersByEntryId.Values
            .SelectMany(judgeIds => judgeIds)
            .Where(judgeId => !activeAtThisTable.Contains(judgeId))
            .ToHashSet();
        var engagedElsewhere = await judgeEngagement.GetJudgesEngagedOutsideTableAsync(
            notification.TableId, coOwnersOffThisTable, cancellationToken);

        return candidates
            .Where(entryId => !coOwnersByEntryId.TryGetValue(entryId, out var coOwners)
                || !coOwners.Any(judgeId => activeAtThisTable.Contains(judgeId) || engagedElsewhere.Contains(judgeId)))
            .ToHashSet();
    }

    private async Task SetFlagAsync(HashSet<Guid> entryIds, bool notValidForBos, CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            return;
        }

        var entries = await dbContext.BeerEntries
            .Where(e => entryIds.Contains(e.Id))
            .ToListAsync(cancellationToken);
        foreach (var entry in entries)
        {
            entry.NotValidForBos = notValidForBos;
        }
    }

    private static HashSet<Guid> EntriesOwnedBy(
        IEnumerable<Guid> judgeIds, IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> ownedEntriesByJudgeId)
    {
        var result = new HashSet<Guid>();
        foreach (var judgeId in judgeIds)
        {
            if (ownedEntriesByJudgeId.TryGetValue(judgeId, out var entries))
            {
                result.UnionWith(entries);
            }
        }

        return result;
    }
}
