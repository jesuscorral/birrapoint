using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Features.Tables.CommonComponents.Dtos;

internal sealed record TableDto(
    Guid Id,
    string Name,
    TableState State,
    IReadOnlyList<TableJudgeDto> Judges,
    IReadOnlyList<TableSampleDto> Samples,
    TableProgressDto Progress,
    TableStatsDto Stats);

