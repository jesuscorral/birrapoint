namespace BirraPoint.Api.Features.Tables.CommonComponents.Dtos;

internal sealed record TableStatsDto(decimal? MeanAbv, int StyleCount, IReadOnlyList<string> Styles);
