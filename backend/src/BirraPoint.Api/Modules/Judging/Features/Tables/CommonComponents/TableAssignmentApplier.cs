using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents.Dtos;
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

        var conflicts = FindConflicts(submittedJudgeEmailsById, beerEntryIds, submittedEntryOwnerEmailsById);
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
        var ownedEntriesByEmail = await BuildOwnedEntriesByEmailAsync(dbContext, table.CompetitionId, cancellationToken);
        var ownedEntriesByJudgeId = relevantJudgeIds.ToDictionary(
            id => id,
            id => judgeEmailsById.TryGetValue(id, out var email) && ownedEntriesByEmail.TryGetValue(email, out var owned)
                ? (IReadOnlySet<Guid>)owned
                : new HashSet<Guid>());

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
            var entryOwnerEmailsById = InvertOwnedEntriesByEmail(ownedEntriesByEmail);
            var stillEligible = new HashSet<Guid>();

            foreach (var entryId in unflagCandidateEntryIds)
            {
                var ownerEmails = entryOwnerEmailsById.TryGetValue(entryId, out var emails)
                    ? emails
                    : (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal);

                var coOwnerJudgeIds = await dbContext.Judges
                    .Where(j => j.CompetitionId == table.CompetitionId && ownerEmails.Contains(j.Email))
                    .Select(j => j.Id)
                    .ToListAsync(cancellationToken);

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

    private static Dictionary<Guid, IReadOnlySet<string>> InvertOwnedEntriesByEmail(
        IReadOnlyDictionary<string, HashSet<Guid>> ownedEntriesByEmail)
    {
        var result = new Dictionary<Guid, HashSet<string>>();

        foreach (var (email, entryIds) in ownedEntriesByEmail)
        {
            foreach (var entryId in entryIds)
            {
                if (!result.TryGetValue(entryId, out var emails))
                {
                    emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    result[entryId] = emails;
                }

                emails.Add(email);
            }
        }

        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<string>)kv.Value);
    }

    private static List<TableConflict> FindConflicts(
        IReadOnlyDictionary<Guid, string> judgeEmailsById,
        IReadOnlyList<Guid> beerEntryIds,
        Dictionary<Guid, IReadOnlySet<string>> entryOwnerEmailsById)
    {
        var conflicts = new List<TableConflict>();

        foreach (var (judgeId, judgeEmail) in judgeEmailsById)
        {
            var conflictingEntryIds = beerEntryIds
                .Where(entryId => entryOwnerEmailsById.TryGetValue(entryId, out var emails) && emails.Contains(judgeEmail))
                .ToList();

            if (conflictingEntryIds.Count > 0)
            {
                conflicts.Add(new TableConflict(judgeId, conflictingEntryIds));
            }
        }

        return conflicts;
    }

    private static bool IsEligibleForUnflag(int remainingActiveTableAssignments, bool hasSubmittedAnyEvaluation) =>
        remainingActiveTableAssignments == 0 && !hasSubmittedAnyEvaluation;

    /// <summary>Union of the entries owned (as participant or collaborator) by the given judges.</summary>
    private static HashSet<Guid> EntriesOwnedByJudges(
        IEnumerable<Guid> judgeIds, Dictionary<Guid, IReadOnlySet<Guid>> ownedEntriesByJudgeId)
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
