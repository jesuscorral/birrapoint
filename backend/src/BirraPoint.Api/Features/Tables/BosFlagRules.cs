namespace BirraPoint.Api.Features.Tables;

/// <summary>
/// Pure BOS flag/unflag rules (FR-018): flagging fires for any judge newly holding at least one
/// active table assignment; unflagging is eligible only once a judge holds zero remaining active
/// assignments AND has never submitted an evaluation in the competition — the flag becomes
/// permanent the moment any evaluation is submitted, regardless of later assignment changes.
/// </summary>
public static class BosFlagRules
{
    public static bool IsEligibleForUnflag(int remainingActiveTableAssignments, bool hasSubmittedAnyEvaluation) =>
        remainingActiveTableAssignments == 0 && !hasSubmittedAnyEvaluation;

    /// <summary>Union of the entries owned (as participant or collaborator) by the given judges.</summary>
    public static IReadOnlySet<Guid> EntriesOwnedByJudges(
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
