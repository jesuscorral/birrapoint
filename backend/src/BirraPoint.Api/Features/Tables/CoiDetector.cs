namespace BirraPoint.Api.Features.Tables;

public sealed record TableConflict(Guid JudgeId, IReadOnlyList<Guid> BeerEntryIds);

/// <summary>
/// Pure COI classification (FR-017): a judge conflicts with a beer entry when the judge's email
/// matches the entry's owner or any collaborator. Callers are responsible for building
/// <paramref name="entryOwnerEmailsById"/> sets with a case-insensitive comparer.
/// </summary>
public static class CoiDetector
{
    public static IReadOnlyList<TableConflict> FindConflicts(
        IReadOnlyDictionary<Guid, string> judgeEmailsById,
        IReadOnlyList<Guid> beerEntryIds,
        IReadOnlyDictionary<Guid, IReadOnlySet<string>> entryOwnerEmailsById)
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
}
