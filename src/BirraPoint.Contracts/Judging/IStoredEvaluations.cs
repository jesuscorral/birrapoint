using BirraPoint.Contracts.Judging.Dtos;

namespace BirraPoint.Contracts.Judging;

/// <summary>Read surface over stored evaluations for other modules (Publication score sheets, Monitoring audit).</summary>
public interface IStoredEvaluations
{
    /// <summary>Every stored evaluation for the requested entries, regardless of <c>Status</c> or table state;
    /// callers that need locked/closed sheets must filter themselves. Entries with none map to an empty
    /// list. Descriptors are already deserialized.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<StoredEvaluationDto>>> GetByEntriesAsync(
        IReadOnlyCollection<Guid> beerEntryIds, CancellationToken cancellationToken = default);
}
