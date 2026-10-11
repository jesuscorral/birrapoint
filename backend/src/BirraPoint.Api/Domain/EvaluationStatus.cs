namespace BirraPoint.Api.Domain;

/// <summary>The spec's "held as provisional" state is PendingConsensus (data-model.md).</summary>
internal enum EvaluationStatus
{
    Confirmed,
    PendingConsensus,
}
