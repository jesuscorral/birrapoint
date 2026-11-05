namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>Anonymous read surface over competition entries for tasting tables (BR-01/FR-019).</summary>
internal interface IEntryDirectory
{
    /// <summary>Sample data keyed by entry id; unknown ids are absent.</summary>
    Task<IReadOnlyDictionary<Guid, EntrySampleInfo>> GetSamplesAsync(
        IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken = default);

    /// <summary>The subset of <paramref name="entryIds"/> that are entries of <paramref name="competitionId"/>.</summary>
    Task<IReadOnlySet<Guid>> GetCompetitionEntryIdsAsync(
        Guid competitionId, IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken = default);
}
