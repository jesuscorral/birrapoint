namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>Port owned by Organization, implemented by Judging: which table each entry sits at (entries list).</summary>
internal interface IEntryTableAssignments
{
    /// <summary>Assignments for the given entries of a competition, keyed by entry id. Unassigned entries
    /// are absent.</summary>
    Task<IReadOnlyDictionary<Guid, EntryTableAssignment>> GetByEntryAsync(
        Guid competitionId,
        IReadOnlyCollection<Guid> beerEntryIds,
        CancellationToken cancellationToken = default);
}
