using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents;

internal static class TableAssignmentApplier
{
    /// <summary>Validates COI and stages the table's judge/sample diff plus the competition-wide
    /// BOS flag changes on <paramref name="dbContext"/>. Does not save: the calling handler owns
    /// the unit of work and calls SaveChangesAsync. Throws ConflictOfInterest before staging
    /// anything.</summary>
    public static async Task<IReadOnlyList<Guid>> ApplyAsync(
        AppDbContext dbContext,
        IConflictOfInterestChecker conflictChecker,
        TastingTable table,
        IReadOnlyList<Guid> judgeIds,
        IReadOnlyList<Guid> beerEntryIds,
        CancellationToken cancellationToken)
    {
        var judgeIdSet = judgeIds.ToHashSet();
        var beerEntryIdSet = beerEntryIds.ToHashSet();
        var currentJudgeIds = table.Judges.Where(tj => tj.RemovedAt == null).Select(tj => tj.JudgeId).ToHashSet();
        var relevantJudgeIds = judgeIdSet.Union(currentJudgeIds).ToHashSet();

        var conflicts = await conflictChecker.FindConflictsAsync(
            table.CompetitionId, judgeIdSet, beerEntryIds, cancellationToken);
        if (conflicts.Count > 0)
        {
            throw new DomainException(
                DomainErrorType.ConflictOfInterest,
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

        // --- BOS flag/unflag, competition-wide (FR-018) ---
        var ownedEntriesByJudgeId = await conflictChecker.GetEntriesOwnedByJudgesAsync(
            table.CompetitionId, relevantJudgeIds, cancellationToken);

        var flaggedEntryIds = EntriesOwnedByJudges(judgesToAdd, ownedEntriesByJudgeId);

        var unflagCandidateEntryIds = new HashSet<Guid>();
        foreach (var row in judgeRowsToRemove)
        {
            if (ownedEntriesByJudgeId.TryGetValue(row.JudgeId, out var owned))
            {
                unflagCandidateEntryIds.UnionWith(owned);
            }
        }

        unflagCandidateEntryIds.ExceptWith(flaggedEntryIds);

        if (unflagCandidateEntryIds.Count > 0)
        {
            var coOwnerJudgesByEntryId = await conflictChecker.GetCoOwnerJudgesAsync(
                table.CompetitionId, unflagCandidateEntryIds, cancellationToken);
            var stillEligible = new HashSet<Guid>();

            foreach (var entryId in unflagCandidateEntryIds)
            {
                var coOwnerJudgeIds = coOwnerJudgesByEntryId.TryGetValue(entryId, out var coOwners)
                    ? coOwners
                    : new HashSet<Guid>();

                var anyCoOwnerStillKeepsFlagAlive = false;
                foreach (var coOwnerJudgeId in coOwnerJudgeIds)
                {
                    // Staying on (or newly added to) THIS table — the mutated table's own
                    // judgeIdSet reflects the desired end state, ahead of SaveChangesAsync.
                    if (judgeIdSet.Contains(coOwnerJudgeId))
                    {
                        anyCoOwnerStillKeepsFlagAlive = true;
                        break;
                    }

                    // Excludes this table (the diff above already accounts for it) so an
                    // in-flight removal on this same call is never double-counted as "still
                    // active" from a pre-commit query.
                    var remainingElsewhere = await dbContext.TableJudges.CountAsync(
                        tj => tj.JudgeId == coOwnerJudgeId && tj.TastingTableId != table.Id && tj.RemovedAt == null,
                        cancellationToken);
                    var hasSubmittedEvaluation = await dbContext.Evaluations.AnyAsync(
                        e => e.JudgeId == coOwnerJudgeId, cancellationToken);

                    if (!IsEligibleForUnflag(remainingElsewhere, hasSubmittedEvaluation))
                    {
                        anyCoOwnerStillKeepsFlagAlive = true;
                        break;
                    }
                }

                if (!anyCoOwnerStillKeepsFlagAlive)
                {
                    stillEligible.Add(entryId);
                }
            }

            unflagCandidateEntryIds = stillEligible;
        }

        if (flaggedEntryIds.Count > 0)
        {
            var entriesToFlag = await dbContext.BeerEntries
                .Where(e => flaggedEntryIds.Contains(e.Id))
                .ToListAsync(cancellationToken);
            foreach (var entry in entriesToFlag)
            {
                entry.NotValidForBos = true;
            }
        }

        if (unflagCandidateEntryIds.Count > 0)
        {
            var entriesToUnflag = await dbContext.BeerEntries
                .Where(e => unflagCandidateEntryIds.Contains(e.Id))
                .ToListAsync(cancellationToken);
            foreach (var entry in entriesToUnflag)
            {
                entry.NotValidForBos = false;
            }
        }

        return flaggedEntryIds.ToList();
    }

    private static bool IsEligibleForUnflag(int remainingActiveTableAssignments, bool hasSubmittedAnyEvaluation) =>
        remainingActiveTableAssignments == 0 && !hasSubmittedAnyEvaluation;

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
