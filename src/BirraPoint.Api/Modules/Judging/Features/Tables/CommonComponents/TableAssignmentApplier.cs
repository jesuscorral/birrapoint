using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Judging.Errors;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Contracts.Events;
using MediatR;

namespace BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents;

internal static class TableAssignmentApplier
{
    /// <summary>Validates COI, stages the table's judge/sample diff on <paramref name="dbContext"/> and
    /// publishes <see cref="TableJudgesChanged"/> so Organization stages the competition-wide BOS flag
    /// changes too. Does not save: the calling handler owns the unit of work and calls SaveChangesAsync.
    /// Throws ConflictOfInterest before staging anything. Returns the entries owned by added judges
    /// (now not valid for BOS).</summary>
    public static async Task<IReadOnlyList<Guid>> ApplyAsync(
        AppDbContext dbContext,
        IConflictOfInterestChecker conflictChecker,
        IPublisher publisher,
        TastingTable table,
        IReadOnlyList<Guid> judgeIds,
        IReadOnlyList<Guid> beerEntryIds,
        CancellationToken cancellationToken)
    {
        var judgeIdSet = judgeIds.ToHashSet();
        var beerEntryIdSet = beerEntryIds.ToHashSet();
        var currentJudgeIds = table.Judges.Where(tj => tj.RemovedAt == null).Select(tj => tj.JudgeId).ToHashSet();

        var conflicts = await conflictChecker.FindConflictsAsync(
            table.CompetitionId, judgeIdSet, beerEntryIds, cancellationToken);
        if (conflicts.Count > 0)
        {
            throw new JudgingException(
                JudgingError.ConflictOfInterest,
                "One or more judges have a conflict of interest with the beers assigned to this table.",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["conflicts"] = conflicts
                        .Select(c => new Dictionary<string, object?>(StringComparer.Ordinal) { ["judgeId"] = c.JudgeId, ["beerEntryIds"] = c.BeerEntryIds })
                        .ToList(),
                });
        }

        // --- diff judges ---
        var judgesToAdd = judgeIdSet.Except(currentJudgeIds).ToList();
        var judgeRowsToRemove = table.Judges.Where(tj => tj.RemovedAt == null && !judgeIdSet.Contains(tj.JudgeId)).ToList();

        foreach (var judgeId in judgesToAdd)
        {
            table.Judges.Add(new TableJudge { TastingTableId = table.Id, JudgeId = judgeId });
        }

        foreach (var row in judgeRowsToRemove)
        {
            dbContext.TableJudges.Remove(row);
        }

        // --- diff samples ---
        var currentEntryIds = table.Samples.Select(ts => ts.BeerEntryId).ToHashSet();
        var entriesToAdd = beerEntryIdSet.Except(currentEntryIds).ToList();
        var sampleRowsToRemove = table.Samples.Where(ts => !beerEntryIdSet.Contains(ts.BeerEntryId)).ToList();

        foreach (var entryId in entriesToAdd)
        {
            table.Samples.Add(new TableSample { TastingTableId = table.Id, BeerEntryId = entryId });
        }

        foreach (var row in sampleRowsToRemove)
        {
            dbContext.TableSamples.Remove(row);
        }

        // --- BOS flag/unflag, competition-wide (FR-018): Organization owns the flag and updates it in
        // this same unit of work; the response only needs the entries the added judges own. ---
        var removedJudgeIds = judgeRowsToRemove.Select(row => row.JudgeId).ToList();
        var ownedEntriesByJudgeId = await conflictChecker.GetEntriesOwnedByJudgesAsync(
            table.CompetitionId, judgesToAdd.Union(removedJudgeIds).ToList(), cancellationToken);

        await publisher.Publish(
            new TableJudgesChanged(
                table.CompetitionId, table.Id, judgeIdSet, judgesToAdd, removedJudgeIds, ownedEntriesByJudgeId),
            cancellationToken);

        return EntriesOwnedByJudges(judgesToAdd, ownedEntriesByJudgeId).ToList();
    }

    /// <summary>Union of the entries owned (as participant or collaborator) by the given judges.</summary>
    private static HashSet<Guid> EntriesOwnedByJudges(
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
