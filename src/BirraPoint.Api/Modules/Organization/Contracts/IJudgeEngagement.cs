namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>Port owned by Organization, implemented by Judging: whether a judge still takes part in the
/// tasting, which keeps the entries they own out of Best of Show (FR-018).</summary>
internal interface IJudgeEngagement
{
    /// <summary>The judges (from <paramref name="judgeIds"/>) with an active seat at a table other than
    /// <paramref name="excludedTableId"/>, or with any submitted evaluation. The excluded table is the one
    /// whose new judge set the caller already knows (not saved yet).</summary>
    Task<IReadOnlySet<Guid>> GetJudgesEngagedOutsideTableAsync(
        Guid excludedTableId, IReadOnlyCollection<Guid> judgeIds, CancellationToken cancellationToken = default);
}
