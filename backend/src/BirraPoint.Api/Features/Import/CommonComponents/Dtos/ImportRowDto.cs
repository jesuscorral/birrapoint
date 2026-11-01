namespace BirraPoint.Api.Features.Import.CommonComponents.Dtos;

/// <summary>Wire shape `{ rowNumber, status, data, error? }` (contracts/rest-api.md §Entry Import).</summary>
internal sealed record ImportRowDto(int RowNumber, ImportRowStatus Status, ImportRowDataDto Data, string? Error)
{
    public static ImportRowDto FromEntity(ImportRow row) => new(
        row.RowNumber,
        row.Status,
        new ImportRowDataDto(
            row.ParticipantName,
            row.ParticipantEmail,
            row.AcceMemberNumberText,
            row.DateOfBirth,
            row.Phone,
            row.CategoryText,
            row.ResolvedCompetitionCategoryId,
            row.StyleText,
            row.ResolvedStyleCode,
            row.SubmittedAt,
            row.AbvPercent,
            row.BrewDate,
            row.BottlingDate,
            row.Malts,
            row.Hops,
            row.Yeast,
            row.OtherIngredients,
            row.EntryInstructions,
            row.BeerName),
        row.ErrorMessage);
}
