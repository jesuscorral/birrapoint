namespace BirraPoint.Api.Modules.Organization.Integration;

/// <summary>FR-018, pure part: an entry is not valid for Best of Show while any judge who owns it (as
/// participant or collaborator) still takes part in the tasting. It depends only on current state, never on
/// the order the changes arrived in (FR-079).</summary>
internal static class BosFlagRules
{
    /// <summary>For every entry, whether it must be flagged: some co-owner judge is in <paramref name="engagedJudgeIds"/>.
    /// An entry absent from <paramref name="coOwnerJudgesByEntryId"/> has no co-owner judge and is not flagged.</summary>
    public static IReadOnlyDictionary<Guid, bool> Compute(
        IEnumerable<Guid> entryIds,
        IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> coOwnerJudgesByEntryId,
        IReadOnlySet<Guid> engagedJudgeIds) =>
        entryIds.Distinct().ToDictionary(
            entryId => entryId,
            entryId => coOwnerJudgesByEntryId.TryGetValue(entryId, out var coOwners) && coOwners.Any(engagedJudgeIds.Contains));
}
