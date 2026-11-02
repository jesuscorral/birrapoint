namespace BirraPoint.Api.Modules.Judging.Contracts.Dtos;

/// <summary>One judge's evaluation of an entry, as exposed to other modules (audit drill-down, score
/// sheets). Carries the judge's display name; never entrant fields.</summary>
internal sealed record FinalizedEvaluationDto(
    Guid BeerEntryId,
    string JudgeDisplayName,
    int AromaScore, string AromaComment,
    int AppearanceScore, string AppearanceComment,
    int FlavorScore, string FlavorComment,
    int MouthfeelScore, string MouthfeelComment,
    int OverallScore, string OverallComment,
    int Total,
    string Status,
    EvaluationDescriptorsDto? Descriptors,
    string? Feedback);
