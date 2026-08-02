namespace BirraPoint.Api.Features.Judges;

/// <summary>Round-trips every editable/raw-parsed cell value, for the judge-roster Mapping &amp;
/// Correction screen (contracts/rest-api.md §Judge Roster Import).</summary>
public sealed record JudgeImportRowDataDto(
    string? Name,
    string? Email,
    string? BjcpRank,
    string? BjcpId,
    string? PreferredCategory,
    string? Preferences);

/// <summary>Wire shape `{ rowNumber, status, data, error? }` (contracts/rest-api.md §Judge Roster Import).</summary>
public sealed record JudgeImportRowDto(int RowNumber, JudgeImportRowStatus Status, JudgeImportRowDataDto Data, string? Error)
{
    public static JudgeImportRowDto FromEntity(JudgeImportRow row) => new(
        row.RowNumber,
        row.Status,
        new JudgeImportRowDataDto(row.Name, row.Email, row.BjcpRank, row.BjcpId, row.PreferredCategory, row.Preferences),
        row.ErrorMessage);
}

/// <summary>Wire shape `{ importId, rows: [...] }` shared by upload and GET (contracts/rest-api.md §Judge Roster Import).</summary>
public sealed record JudgeImportBatchDto(Guid ImportId, IReadOnlyList<JudgeImportRowDto> Rows)
{
    public static JudgeImportBatchDto FromEntity(JudgeImportBatch batch) => new(
        batch.Id,
        batch.Rows.OrderBy(row => row.RowNumber).Select(JudgeImportRowDto.FromEntity).ToList());
}
