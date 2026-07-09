using Microsoft.AspNetCore.Http;

namespace BirraPoint.Api.Common.Errors;

/// <summary>Urn/status/title metadata for each <see cref="DomainErrorType"/>, per contracts/rest-api.md §Error catalog.</summary>
public static class DomainErrorCatalog
{
    public static readonly IReadOnlyDictionary<DomainErrorType, (string Urn, int Status, string Title)> Entries =
        new Dictionary<DomainErrorType, (string, int, string)>
        {
            [DomainErrorType.Validation] = ("urn:birrapoint:validation", StatusCodes.Status400BadRequest, "Validation failed"),
            [DomainErrorType.InvalidImportFile] = ("urn:birrapoint:invalid-import-file", StatusCodes.Status400BadRequest, "Invalid import file"),
            [DomainErrorType.InvalidStateTransition] = ("urn:birrapoint:invalid-state-transition", StatusCodes.Status409Conflict, "Invalid state transition"),
            [DomainErrorType.ConflictOfInterest] = ("urn:birrapoint:conflict-of-interest", StatusCodes.Status409Conflict, "Conflict of interest"),
            [DomainErrorType.UnresolvedImportRows] = ("urn:birrapoint:unresolved-import-rows", StatusCodes.Status409Conflict, "Unresolved import rows"),
            [DomainErrorType.OrderAlreadyFixed] = ("urn:birrapoint:order-already-fixed", StatusCodes.Status409Conflict, "Tasting order already fixed"),
            [DomainErrorType.OrderNotFixed] = ("urn:birrapoint:order-not-fixed", StatusCodes.Status409Conflict, "Tasting order not fixed"),
            [DomainErrorType.OutOfSequence] = ("urn:birrapoint:out-of-sequence", StatusCodes.Status409Conflict, "Sample out of sequence"),
            [DomainErrorType.EvaluationLocked] = ("urn:birrapoint:evaluation-locked", StatusCodes.Status409Conflict, "Evaluation locked"),
            [DomainErrorType.TableClosed] = ("urn:birrapoint:table-closed", StatusCodes.Status409Conflict, "Tasting table closed"),
            [DomainErrorType.EvaluationsIncomplete] = ("urn:birrapoint:evaluations-incomplete", StatusCodes.Status409Conflict, "Evaluations incomplete"),
            [DomainErrorType.DiscrepancyOpen] = ("urn:birrapoint:discrepancy-open", StatusCodes.Status409Conflict, "Discrepancy still open"),
            [DomainErrorType.TablesStillOpen] = ("urn:birrapoint:tables-still-open", StatusCodes.Status409Conflict, "Tables still open"),
            [DomainErrorType.JudgeAlreadyActive] = ("urn:birrapoint:judge-already-active", StatusCodes.Status409Conflict, "Judge already active"),
        };
}
