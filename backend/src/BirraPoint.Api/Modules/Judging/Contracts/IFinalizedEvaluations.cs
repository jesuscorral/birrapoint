using BirraPoint.Api.Modules.Judging.Contracts.Dtos;

namespace BirraPoint.Api.Modules.Judging.Contracts;

/// <summary>Read surface over stored evaluations for other modules (Publication score sheets, Monitoring audit).</summary>
internal interface IFinalizedEvaluations
{
    /// <summary>Evaluations per entry id; entries with none map to an empty list. Descriptors are
    /// already deserialized.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<FinalizedEvaluationDto>>> GetByEntriesAsync(
        IReadOnlyCollection<Guid> beerEntryIds, CancellationToken cancellationToken = default);
}
