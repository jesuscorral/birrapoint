namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>Read surface over competitions for other modules (ownership scoping, state gates, dispatch jobs).</summary>
internal interface ICompetitionAccess
{
    /// <summary>The competition, or null when it is missing or not owned by <paramref name="ownerUserId"/>
    /// (Keycloak sub); callers answer 404.</summary>
    Task<CompetitionSnapshot?> GetOwnedAsync(
        Guid competitionId, string ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>The competition without an ownership check: judge-side state gates and background dispatch jobs.</summary>
    Task<CompetitionSnapshot?> GetAsync(Guid competitionId, CancellationToken cancellationToken = default);

    /// <summary>Batch state lookup; unknown ids are absent; empty input returns an empty dictionary without a query.</summary>
    Task<IReadOnlyDictionary<Guid, CompetitionState>> GetStatesAsync(
        IReadOnlyCollection<Guid> competitionIds, CancellationToken cancellationToken = default);
}
