using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Tables;

/// <summary>
/// Shared CreateTable/UpdateTable core (FR-016/017/018). Validates COI atomically over the full
/// submitted judge/entry sets before any mutation (nothing persisted on conflict — no
/// SaveChangesAsync has run yet), diffs the submitted sets against the table's current
/// TableJudge/TableSample rows, then flags/unflags BOS eligibility competition-wide as table
/// membership changes. Callers are expected to have already added <paramref name="table"/> to the
/// context (for a new table) or loaded it tracked with its Judges/Samples included (for an
/// update) before calling this.
/// </summary>
internal static class TableAssignmentApplier
{
    public static async Task<IReadOnlyList<Guid>> ApplyAsync(
        AppDbContext dbContext,
        TastingTable table,
        IReadOnlyList<Guid> judgeIds,
        IReadOnlyList<Guid> beerEntryIds,
        CancellationToken cancellationToken)
    {
        var judgeIdSet = judgeIds.ToHashSet();
        var beerEntryIdSet = beerEntryIds.ToHashSet();
        var currentJudgeIds = table.Judges.Where(tj => tj.RemovedAt == null).Select(tj => tj.JudgeId).ToHashSet();
        var relevantJudgeIds = judgeIdSet.Union(currentJudgeIds).ToHashSet();

        var judgeEmailsById = await dbContext.Judges
            .Where(j => relevantJudgeIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => j.Email, cancellationToken);

        var entries = await dbContext.BeerEntries
            .Include(e => e.Collaborators)
            .Where(e => beerEntryIdSet.Contains(e.Id))
            .ToListAsync(cancellationToken);

        var participantsById = await dbContext.Participants
            .Where(p => entries.Select(e => e.ParticipantId).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var submittedEntryOwnerEmailsById = entries.ToDictionary(
            e => e.Id,
            e =>
            {
                var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { participantsById[e.ParticipantId].Email };
                foreach (var collaborator in e.Collaborators)
                {
                    emails.Add(collaborator.Email);
                }

                return (IReadOnlySet<string>)emails;
            });

        var submittedJudgeEmailsById = judgeIdSet.ToDictionary(id => id, id => judgeEmailsById[id]);

        var conflicts = CoiDetector.FindConflicts(submittedJudgeEmailsById, beerEntryIds, submittedEntryOwnerEmailsById);
        if (conflicts.Count > 0)
        {
            throw new DomainException(
                DomainErrorType.ConflictOfInterest,
                "One or more judges have a conflict of interest with the beers assigned to this table.",
                new Dictionary<string, object?>
                {
                    ["conflicts"] = conflicts
                        .Select(c => new Dictionary<string, object?> { ["judgeId"] = c.JudgeId, ["beerEntryIds"] = c.BeerEntryIds })
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
        var ownedEntriesByEmail = await BuildOwnedEntriesByEmailAsync(dbContext, table.CompetitionId, cancellationToken);
        var ownedEntriesByJudgeId = relevantJudgeIds.ToDictionary(
            id => id,
            id => judgeEmailsById.TryGetValue(id, out var email) && ownedEntriesByEmail.TryGetValue(email, out var owned)
                ? (IReadOnlySet<Guid>)owned
                : new HashSet<Guid>());

        var flaggedEntryIds = BosFlagRules.EntriesOwnedByJudges(judgesToAdd, ownedEntriesByJudgeId);

        var unflagCandidateEntryIds = new HashSet<Guid>();
        foreach (var row in judgeRowsToRemove)
        {
            var remainingAssignments = await dbContext.TableJudges.CountAsync(
                tj => tj.JudgeId == row.JudgeId && tj.TastingTableId != table.Id && tj.RemovedAt == null,
                cancellationToken);
            var hasSubmittedEvaluation = await dbContext.Evaluations.AnyAsync(
                e => e.JudgeId == row.JudgeId, cancellationToken);

            if (!BosFlagRules.IsEligibleForUnflag(remainingAssignments, hasSubmittedEvaluation))
            {
                continue;
            }

            if (ownedEntriesByJudgeId.TryGetValue(row.JudgeId, out var owned))
            {
                unflagCandidateEntryIds.UnionWith(owned);
            }
        }

        // An entry co-owned by both a judge leaving this table and a judge just added to it (or
        // still active) must stay flagged — never let the leaving judge's unflag win.
        unflagCandidateEntryIds.ExceptWith(flaggedEntryIds);

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

        await dbContext.SaveChangesAsync(cancellationToken);

        return flaggedEntryIds.ToList();
    }

    private static async Task<IReadOnlyDictionary<string, HashSet<Guid>>> BuildOwnedEntriesByEmailAsync(
        AppDbContext dbContext, Guid competitionId, CancellationToken cancellationToken)
    {
        var ownerRows = await dbContext.BeerEntries
            .Where(e => e.CompetitionId == competitionId)
            .Join(dbContext.Participants, e => e.ParticipantId, p => p.Id, (e, p) => new { e.Id, p.Email })
            .ToListAsync(cancellationToken);

        var collaboratorRows = await dbContext.EntryCollaborators
            .Join(
                dbContext.BeerEntries.Where(e => e.CompetitionId == competitionId),
                ec => ec.BeerEntryId,
                e => e.Id,
                (ec, e) => new { e.Id, ec.Email })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in ownerRows.Concat(collaboratorRows))
        {
            if (!map.TryGetValue(row.Email, out var set))
            {
                set = [];
                map[row.Email] = set;
            }

            set.Add(row.Id);
        }

        return map;
    }
}
