namespace BirraPoint.Api.Common.Errors;

/// <summary>
/// The closed set of 14 domain error types from contracts/rest-api.md §Error catalog. Adding a
/// member here requires a contract amendment (Principle VI) — this enum is not meant to grow ad
/// hoc as new slices are added.
/// </summary>
public enum DomainErrorType
{
    Validation,
    InvalidImportFile,
    InvalidStateTransition,
    ConflictOfInterest,
    UnresolvedImportRows,
    OrderAlreadyFixed,
    OrderNotFixed,
    OutOfSequence,
    EvaluationLocked,
    TableClosed,
    EvaluationsIncomplete,
    DiscrepancyOpen,
    TablesStillOpen,
    JudgeAlreadyActive,
}
