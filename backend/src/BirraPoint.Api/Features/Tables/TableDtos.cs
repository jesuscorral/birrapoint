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
    decimal AbvPercent,
    bool NotValidForBos,
    string? EntryInstructions,
    // T124: the organizer-defined competition category (wizard step 3) this entry was imported
    // under — null for entries seeded outside the Import slice, which never get one assigned.
    string? CompetitionCategoryName,
    // T124: the BJCP taxonomy's own category (e.g. "21"/"IPA"), a completely independent axis
    // from CompetitionCategoryName. Nullable defensively only: BeerEntry.StyleCode is a required,
    // non-nullable FK to BjcpStyles.Code with OnDelete(Restrict), and BjcpStyle.CategoryNumber /
    // .CategoryName are both `required string`, so an entry whose style has no catalog row cannot
    // exist. Treat null as unreachable rather than a state clients must handle.
    string? BjcpCategoryNumber,
    string? BjcpCategoryName);

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
