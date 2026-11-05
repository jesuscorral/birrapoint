using BirraPoint.Api.Modules.Judging.Domain;

namespace BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents.Dtos;

internal sealed record TableDto(
    Guid Id,
    string Name,
    TableState State,
    IReadOnlyList<TableJudgeDto> Judges,
    IReadOnlyList<TableSampleDto> Samples,
    TableProgressDto Progress,
    TableStatsDto Stats);

