using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Integration;

/// <summary>FR-018: a judge owns an entry when the judge e-mail equals, case-insensitively, the
/// participant or a collaborator e-mail. Everything is scoped to one competition.</summary>
internal sealed class ConflictOfInterestChecker(AppDbContext dbContext) : IConflictOfInterestChecker
{
    public async Task<IReadOnlyList<JudgeEntryConflict>> FindConflictsAsync(
        Guid competitionId,
        IReadOnlyCollection<Guid> judgeIds,
        IReadOnlyCollection<Guid> beerEntryIds,
        CancellationToken cancellationToken = default)
    {
        if (judgeIds.Count == 0 || beerEntryIds.Count == 0)
        {
            return [];
        }

        var judgeEmails = await LoadJudgeEmailsAsync(competitionId, judgeIds, cancellationToken);
        var ownerEmailsByEntry = await LoadOwnerEmailsAsync(competitionId, beerEntryIds, cancellationToken);

        var conflicts = new List<JudgeEntryConflict>();
        foreach (var judgeId in judgeIds)
        {
            if (!judgeEmails.TryGetValue(judgeId, out var judgeEmail))
            {
                continue;
            }

            var conflicting = beerEntryIds
                .Where(entryId => ownerEmailsByEntry.TryGetValue(entryId, out var owners) && owners.Contains(judgeEmail))
                .ToList();
            if (conflicting.Count > 0)
            {
                conflicts.Add(new JudgeEntryConflict(judgeId, conflicting));
            }
        }

        return conflicts;
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlySet<Guid>>> GetEntriesOwnedByJudgesAsync(
        Guid competitionId,
        IReadOnlyCollection<Guid> judgeIds,
        CancellationToken cancellationToken = default)
    {
        if (judgeIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlySet<Guid>>();
        }

        var judgeEmails = await LoadJudgeEmailsAsync(competitionId, judgeIds, cancellationToken);
        var ownerEmailsByEntry = await LoadOwnerEmailsAsync(competitionId, entryIds: null, cancellationToken);

        return judgeIds.Distinct().ToDictionary(
            id => id,
            id => judgeEmails.TryGetValue(id, out var email)
                ? (IReadOnlySet<Guid>)ownerEmailsByEntry.Where(kv => kv.Value.Contains(email)).Select(kv => kv.Key).ToHashSet()
                : new HashSet<Guid>());
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlySet<Guid>>> GetCoOwnerJudgesAsync(
        Guid competitionId,
        IReadOnlyCollection<Guid> beerEntryIds,
        CancellationToken cancellationToken = default)
    {
        if (beerEntryIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlySet<Guid>>();
        }

        var ownerEmailsByEntry = await LoadOwnerEmailsAsync(competitionId, beerEntryIds, cancellationToken);
        var judges = await dbContext.Judges
            .AsNoTracking()
            .Where(j => j.CompetitionId == competitionId)
            .Select(j => new { j.Id, j.Email })
            .ToListAsync(cancellationToken);

        return beerEntryIds.Distinct().ToDictionary(
            id => id,
            id => ownerEmailsByEntry.TryGetValue(id, out var owners)
                ? (IReadOnlySet<Guid>)judges.Where(j => owners.Contains(j.Email)).Select(j => j.Id).ToHashSet()
                : new HashSet<Guid>());
    }

    private async Task<Dictionary<Guid, string>> LoadJudgeEmailsAsync(
        Guid competitionId, IReadOnlyCollection<Guid> judgeIds, CancellationToken cancellationToken)
    {
        var ids = judgeIds.Distinct().ToList();
        return await dbContext.Judges
            .AsNoTracking()
            .Where(j => j.CompetitionId == competitionId && ids.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => j.Email, cancellationToken);
    }

    /// <summary>Owner e-mails (participant plus collaborators) per entry of the competition, in one
    /// round trip; a null <paramref name="entryIds"/> means every entry of the competition.</summary>
    private async Task<Dictionary<Guid, HashSet<string>>> LoadOwnerEmailsAsync(
        Guid competitionId, IReadOnlyCollection<Guid>? entryIds, CancellationToken cancellationToken)
    {
        var ids = entryIds?.Distinct().ToList();
        var entries = dbContext.BeerEntries
            .AsNoTracking()
            .Where(e => e.CompetitionId == competitionId && (ids == null || ids.Contains(e.Id)));

        var participantRows = entries
            .Join(dbContext.Participants, e => e.ParticipantId, p => p.Id, (e, p) => new { EntryId = e.Id, p.Email });
        var collaboratorRows = entries
            .Join(dbContext.EntryCollaborators, e => e.Id, c => c.BeerEntryId, (e, c) => new { EntryId = e.Id, c.Email });

        var rows = await participantRows.Concat(collaboratorRows).ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, HashSet<string>>();
        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.EntryId, out var emails))
            {
                emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                result[row.EntryId] = emails;
            }

            emails.Add(row.Email);
        }

        return result;
    }
}
