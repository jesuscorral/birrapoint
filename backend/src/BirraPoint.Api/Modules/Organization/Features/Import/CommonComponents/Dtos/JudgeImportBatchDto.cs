namespace BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents.Dtos;

/// <summary>Wire shape `{ importId, rows: [...] }` shared by upload and GET (contracts/rest-api.md §Judge Roster Import).</summary>
internal sealed record JudgeImportBatchDto(Guid ImportId, IReadOnlyList<JudgeImportRowDto> Rows)
{
    public static JudgeImportBatchDto FromEntity(JudgeImportBatch batch) => new(
        batch.Id,
        batch.Rows.OrderBy(row => row.RowNumber).Select(JudgeImportRowDto.FromEntity).ToList());
}
