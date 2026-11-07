namespace BirraPoint.Api.Modules.Judging.Errors;

/// <summary>
/// The Judging module's domain error types, a subset of the closed list in contracts/rest-api.md §Error
/// catalog (Principle VI). Adding a member requires a contract amendment. A URN shared with another
/// module is the wire contract, not code coupling (ADR-0033/ADR-0037).
/// </summary>
internal enum JudgingError
{
    ConflictOfInterest,
    OrderAlreadyFixed,
    OrderNotFixed,
    OutOfSequence,
    EvaluationLocked,
    TableClosed,
    EvaluationsIncomplete,
    DiscrepancyOpen,
    InvalidStateTransition,
}
