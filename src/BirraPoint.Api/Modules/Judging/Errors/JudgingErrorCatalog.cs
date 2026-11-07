namespace BirraPoint.Api.Modules.Judging.Errors;

/// <summary>Urn/status/title metadata for each <see cref="JudgingError"/>, per contracts/rest-api.md §Error catalog.</summary>
internal static class JudgingErrorCatalog
{
    public static readonly IReadOnlyDictionary<JudgingError, (string Urn, int Status, string Title)> Entries =
        new Dictionary<JudgingError, (string, int, string)>
        {
            [JudgingError.ConflictOfInterest] = ("urn:birrapoint:conflict-of-interest", StatusCodes.Status409Conflict, "Conflict of interest"),
            [JudgingError.OrderAlreadyFixed] = ("urn:birrapoint:order-already-fixed", StatusCodes.Status409Conflict, "Tasting order already fixed"),
            [JudgingError.OrderNotFixed] = ("urn:birrapoint:order-not-fixed", StatusCodes.Status409Conflict, "Tasting order not fixed"),
            [JudgingError.OutOfSequence] = ("urn:birrapoint:out-of-sequence", StatusCodes.Status409Conflict, "Sample out of sequence"),
            [JudgingError.EvaluationLocked] = ("urn:birrapoint:evaluation-locked", StatusCodes.Status409Conflict, "Evaluation locked"),
            [JudgingError.TableClosed] = ("urn:birrapoint:table-closed", StatusCodes.Status409Conflict, "Tasting table closed"),
            [JudgingError.EvaluationsIncomplete] = ("urn:birrapoint:evaluations-incomplete", StatusCodes.Status409Conflict, "Evaluations incomplete"),
            [JudgingError.DiscrepancyOpen] = ("urn:birrapoint:discrepancy-open", StatusCodes.Status409Conflict, "Discrepancy still open"),
            [JudgingError.InvalidStateTransition] = ("urn:birrapoint:invalid-state-transition", StatusCodes.Status409Conflict, "Invalid state transition"),
        };
}
