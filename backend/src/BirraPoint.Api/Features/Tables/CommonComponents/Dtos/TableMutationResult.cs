using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Features.Tables.CommonComponents.Dtos;

/// <summary>POST/PUT response: the table plus the entries newly flagged "Not valid for BOS" by this call (FR-018).</summary>
internal sealed record TableMutationResult(
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
