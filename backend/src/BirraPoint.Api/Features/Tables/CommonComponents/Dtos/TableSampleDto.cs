namespace BirraPoint.Api.Features.Tables.CommonComponents.Dtos;

internal sealed record TableSampleDto(
    Guid BeerEntryId,
    string BlindCode,
    string StyleCode,
    string StyleName,
    decimal? AbvLow,
    decimal? AbvHigh,
    decimal AbvPercent,
    bool NotValidForBos,
    string? EntryInstructions,
    string? CompetitionCategoryName,
    string? BjcpCategoryNumber,
    string? BjcpCategoryName);
