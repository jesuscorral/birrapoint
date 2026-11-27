namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>Port owned by Organization, implemented by Judging: whether a judge still takes part in the
/// tasting, which keeps the entries they own out of Best of Show (FR-018).</summary>
internal interface IJudgeEngagement
{
    /// <summary>The judges (from <paramref name="judgeIds"/>) with an active seat at any table of
    /// <paramref name="competitionId"/>, or with any submitted evaluation. Reads persisted state only.</summary>
    Task<IReadOnlySet<Guid>> GetEngagedJudgesAsync(
        Guid competitionId, IReadOnlyCollection<Guid> judgeIds, CancellationToken cancellationToken = default);
}
