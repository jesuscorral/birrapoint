namespace BirraPoint.Api.Features.Evaluations.CommonComponents.Dtos;

/// <summary>Per-judge total for one discrepancy (contracts/rest-api.md §Judge workspace
/// `GET .../discrepancies`). <see cref="EvaluationId"/> is additive beyond the documented wire
/// shape — the frontend needs it to address the PUT adjustment endpoint.</summary>
internal sealed record DiscrepancyTotalDto(string JudgeDisplayName, int Total, bool IsMine, Guid EvaluationId);
