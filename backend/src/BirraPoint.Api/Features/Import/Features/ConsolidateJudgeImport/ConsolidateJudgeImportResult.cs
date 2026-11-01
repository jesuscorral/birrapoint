using BirraPoint.Api.Features.Judges.CommonComponents.Dtos;

namespace BirraPoint.Api.Features.Import.Features.ConsolidateJudgeImport;

internal sealed record ConsolidateJudgeImportResult(
    IReadOnlyList<ConsolidatedJudgeDto> Created,
    IReadOnlyList<ConsolidatedJudgeDto> Updated,
    int Excluded,
    IReadOnlyList<JudgeSkipDto> Skipped);
