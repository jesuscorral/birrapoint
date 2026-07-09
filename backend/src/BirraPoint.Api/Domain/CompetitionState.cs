namespace BirraPoint.Api.Domain;

/// <summary>FR-006: forward-only, skip-free, organizer-only transitions.</summary>
public enum CompetitionState
{
    Draft,
    Active,
    InEvaluation,
    Finalized,
}
