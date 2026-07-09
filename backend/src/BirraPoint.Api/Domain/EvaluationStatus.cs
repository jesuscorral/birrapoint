namespace BirraPoint.Api.Domain;

/// <summary>The spec's "held as provisional" state is PendingConsensus (data-model.md).</summary>
public enum EvaluationStatus
{
    Confirmed,
    PendingConsensus,
}
