namespace BirraPoint.Api.Features.Import.CommonComponents.Dtos;

/// <summary>Wire shape `{ importId, rows: [...] }` shared by upload and GET (contracts/rest-api.md §Entry Import).</summary>
internal sealed record BeerImportBatchDto(Guid ImportId, IReadOnlyList<BeerImportRowDto> Rows)
{
    public static BeerImportBatchDto FromEntity(BeerImportBatch batch) => new(
        batch.Id,
        batch.Rows.OrderBy(row => row.RowNumber).Select(BeerImportRowDto.FromEntity).ToList());
}
