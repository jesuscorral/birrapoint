namespace BirraPoint.Api.Features.Import.CommonComponents.Dtos;

/// <summary>Wire shape `{ importId, rows: [...] }` shared by upload and GET (contracts/rest-api.md §Entry Import).</summary>
internal sealed record ImportBatchDto(Guid ImportId, IReadOnlyList<ImportRowDto> Rows)
{
    public static ImportBatchDto FromEntity(ImportBatch batch) => new(
        batch.Id,
        batch.Rows.OrderBy(row => row.RowNumber).Select(ImportRowDto.FromEntity).ToList());
}
