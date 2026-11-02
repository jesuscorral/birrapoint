namespace BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents.Dtos;

/// <summary>
/// Session 2026-09-21: structured tasting-sheet descriptors, additive alongside the five
/// authoritative section scores/comments (EvaluationScoresDto/EvaluationCommentsDto) — never feed
/// SubmitEvaluationRules' caps or Evaluation.Total, which stay computed from the five scores alone
/// (FR-024, unchanged). Every field here is optional, both at the top level and within each nested
/// section object: a judge can fill as many or as few as they like, same as the paper BJCP sheet
/// this mirrors. Stored as-is (camelCase, this app's own HTTP JSON convention) in
/// Evaluation.DescriptorsJson — see ADR for why a jsonb string column instead of ~25 new columns.
/// </summary>
internal sealed record EvaluationDescriptorsDto(
    AppearanceDescriptorsDto? Appearance,
    AromaDescriptorsDto? Aroma,
    FlavorDescriptorsDto? Flavor,
    MouthfeelDescriptorsDto? Mouthfeel,
    OverallDescriptorsDto? Overall,
    IReadOnlyList<string>? OffFlavors);
