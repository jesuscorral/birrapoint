namespace BirraPoint.Api.Domain;

/// <summary>FR-006: forward-only, skip-free, organizer-only transitions.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "Used as a parameter type of public xUnit [Theory] methods in the test assemblies, which must be public (xUnit1000/CS0051); InternalsVisibleTo cannot relax that.")]
public enum CompetitionState
{
    Draft,
    Active,
    InEvaluation,
    Finalized,
}
