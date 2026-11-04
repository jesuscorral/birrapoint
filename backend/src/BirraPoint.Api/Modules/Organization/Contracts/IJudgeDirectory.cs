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
}
