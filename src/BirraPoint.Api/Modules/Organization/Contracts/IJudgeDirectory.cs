namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>Read surface over competition judges for other modules: the signed-in judge's identities,
/// display data and competition membership.</summary>
internal interface IJudgeDirectory
{
    /// <summary>Every judge record of the signed-in user across competitions (matched by e-mail, Keycloak
    /// identity backfilled on first use); empty when the caller is not a judge anywhere.</summary>
    Task<IReadOnlyList<JudgeSummary>> GetSignedInJudgesAsync(CancellationToken cancellationToken = default);

    /// <summary>Display names keyed by judge id; unknown ids are absent.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> judgeIds, CancellationToken cancellationToken = default);

    /// <summary>Summaries of the given judges ordered by display name with the database collation (accented
    /// Spanish names sort correctly); unknown ids are absent.</summary>
    Task<IReadOnlyList<JudgeSummary>> GetSummariesAsync(
        IReadOnlyCollection<Guid> judgeIds, CancellationToken cancellationToken = default);

    /// <summary>The subset of <paramref name="judgeIds"/> that are judges of <paramref name="competitionId"/>.</summary>
    Task<IReadOnlySet<Guid>> GetCompetitionJudgeIdsAsync(
        Guid competitionId, IReadOnlyCollection<Guid> judgeIds, CancellationToken cancellationToken = default);

    /// <summary>Ids of the judge records (one per competition) of the identity <paramref name="keycloakUserId"/>: matched
    /// by Keycloak id, or, while the record has none yet, by <paramref name="email"/>. The Keycloak id is only backfilled
    /// once a REST call resolves the judge, so a judge whose first action is opening a socket relies on the e-mail
    /// fallback. A null <paramref name="email"/> never matches (it must not select every record without a Keycloak id).
    /// Read-only: no backfill.</summary>
    Task<IReadOnlyList<Guid>> FindJudgeIdsAsync(
        string keycloakUserId, string? email, CancellationToken cancellationToken = default);
}
