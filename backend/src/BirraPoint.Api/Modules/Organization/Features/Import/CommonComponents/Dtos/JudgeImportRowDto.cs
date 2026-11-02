namespace BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents.Dtos;

/// <summary>Wire shape `{ rowNumber, status, data, error? }` (contracts/rest-api.md §Judge Roster Import).</summary>
internal sealed record JudgeImportRowDto(int RowNumber, JudgeImportRowStatus Status, JudgeImportRowDataDto Data, string? Error)
{
    public static JudgeImportRowDto FromEntity(JudgeImportRow row) => new(
        row.RowNumber,
        row.Status,
        new JudgeImportRowDataDto(row.Name, row.Email, row.BjcpRank, row.BjcpId, row.PreferredCategory, row.Preferences),
        row.ErrorMessage);
}
