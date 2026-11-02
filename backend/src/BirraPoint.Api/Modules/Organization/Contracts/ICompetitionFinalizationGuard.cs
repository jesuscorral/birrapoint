namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>Port owned by Organization, implemented by Judging: finalizing a competition requires every
/// tasting table to be closed.</summary>
internal interface ICompetitionFinalizationGuard
{
    /// <summary>Ids of the competition's tasting tables that are not closed yet; empty when it may finalize.</summary>
    Task<IReadOnlyList<Guid>> GetOpenTableIdsAsync(Guid competitionId, CancellationToken cancellationToken = default);
}
