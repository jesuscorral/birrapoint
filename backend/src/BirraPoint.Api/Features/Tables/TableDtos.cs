using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Features.Tables;

public sealed record TableJudgeDto(Guid Id, string Email, string DisplayName);

public sealed record TableSampleDto(
    Guid BeerEntryId,
    string BlindCode,
    string StyleCode,
    string StyleName,
    decimal? AbvLow,
    decimal? AbvHigh,
    bool NotValidForBos);

public sealed record TableProgressDto(int Submitted, int Total);

public sealed record TableStatsDto(decimal? MeanAbv, int StyleCount, IReadOnlyList<string> Styles);

/// <summary>GET /tables shape (contracts/rest-api.md §Tables: "judges, samples, progress, state").</summary>
public sealed record TableDto(
    Guid Id,
    string Name,
    TableState State,
    IReadOnlyList<TableJudgeDto> Judges,
    IReadOnlyList<TableSampleDto> Samples,
    TableProgressDto Progress,
    TableStatsDto Stats);

/// <summary>POST/PUT response: the table plus the entries newly flagged "Not valid for BOS" by this call (FR-018).</summary>
public sealed record TableMutationResult(
    Guid Id,
    string Name,
    TableState State,
    IReadOnlyList<TableJudgeDto> Judges,
    IReadOnlyList<TableSampleDto> Samples,
    TableProgressDto Progress,
    TableStatsDto Stats,
    IReadOnlyList<Guid> BosFlaggedEntryIds)
{
    public static TableMutationResult From(TableDto table, IReadOnlyList<Guid> bosFlaggedEntryIds) =>
        new(table.Id, table.Name, table.State, table.Judges, table.Samples, table.Progress, table.Stats, bosFlaggedEntryIds);
}
