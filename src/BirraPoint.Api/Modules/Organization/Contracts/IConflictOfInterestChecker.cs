namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>
/// Conflict-of-interest queries (FR-018). The rule itself (judge e-mail equals a participant or
/// collaborator e-mail of the entry, case-insensitive) lives in Organization; tasting tables only ask
/// questions about judge ids and entry ids within one competition.
/// </summary>
internal interface IConflictOfInterestChecker
{
    /// <summary>Judges (from <paramref name="judgeIds"/>) that own any of <paramref name="beerEntryIds"/>,
    /// with the entries they conflict on. Empty when there is no conflict.</summary>
    Task<IReadOnlyList<JudgeEntryConflict>> FindConflictsAsync(
        Guid competitionId,
        IReadOnlyCollection<Guid> judgeIds,
        IReadOnlyCollection<Guid> beerEntryIds,
        CancellationToken cancellationToken = default);

    /// <summary>For each judge id, the competition entries that judge owns (empty set when none).
    /// Drives the competition-wide not-valid-for-BOS flag.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlySet<Guid>>> GetEntriesOwnedByJudgesAsync(
        Guid competitionId,
        IReadOnlyCollection<Guid> judgeIds,
        CancellationToken cancellationToken = default);

    /// <summary>For each entry id, the judges of the competition that co-own it (empty set when none).
    /// Used to decide whether the BOS flag can be lifted.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlySet<Guid>>> GetCoOwnerJudgesAsync(
        Guid competitionId,
        IReadOnlyCollection<Guid> beerEntryIds,
        CancellationToken cancellationToken = default);
}
