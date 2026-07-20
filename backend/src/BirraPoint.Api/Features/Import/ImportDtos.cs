using System.Text.Json;

namespace BirraPoint.Api.Features.Import;

/// <summary>Round-trips the raw parsed cell values, for the Mapping &amp; Correction screen (contracts/rest-api.md §Entry Import).</summary>
public sealed record ImportRowDataDto(
    string? ParticipantName,
    string? ParticipantEmail,
    string? BeerName,
    string? Style,
    IReadOnlyList<string> Collaborators,
    string? ResolvedStyleCode);

/// <summary>Wire shape `{ rowNumber, status, data, error? }` (contracts/rest-api.md §Entry Import).</summary>
public sealed record ImportRowDto(int RowNumber, ImportRowStatus Status, ImportRowDataDto Data, string? Error)
{
    public static ImportRowDto FromEntity(ImportRow row) => new(
        row.RowNumber,
        row.Status,
        new ImportRowDataDto(
            row.ParticipantName,
            row.ParticipantEmail,
            row.BeerName,
            row.StyleText,
            JsonSerializer.Deserialize<List<string>>(row.CollaboratorsJson) ?? [],
            row.ResolvedStyleCode),
        row.ErrorMessage);
}

/// <summary>Wire shape `{ importId, rows: [...] }` shared by upload and GET (contracts/rest-api.md §Entry Import).</summary>
public sealed record ImportBatchDto(Guid ImportId, IReadOnlyList<ImportRowDto> Rows)
{
    public static ImportBatchDto FromEntity(ImportBatch batch) => new(
        batch.Id,
        batch.Rows.OrderBy(row => row.RowNumber).Select(ImportRowDto.FromEntity).ToList());
}
