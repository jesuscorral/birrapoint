namespace BirraPoint.Api.Features.Import;

/// <summary>Round-trips every editable/raw-parsed cell value, for the Mapping &amp; Correction
/// screen (contracts/rest-api.md §Entry Import). <see cref="Category"/>/<see cref="Style"/> are
/// the raw parsed cell text (may not resolve to anything); <see cref="CompetitionCategoryId"/>/
/// <see cref="ResolvedStyleCode"/> are the resolved references, set at parse time or by the
/// organizer via the full row edit.</summary>
public sealed record ImportRowDataDto(
    string? ParticipantName,
    string? ParticipantEmail,
    string? AcceMemberNumber,
    DateOnly? DateOfBirth,
    string? Phone,
    string? Category,
    Guid? CompetitionCategoryId,
    string? Style,
    string? ResolvedStyleCode,
    DateTimeOffset? SubmittedAt,
    decimal? AbvPercent,
    DateOnly? BrewDate,
    DateOnly? BottlingDate,
    string? Malts,
    string? Hops,
    string? Yeast,
    string? OtherIngredients,
    string? EntryInstructions,
    string? BeerName);

/// <summary>Wire shape `{ rowNumber, status, data, error? }` (contracts/rest-api.md §Entry Import).</summary>
public sealed record ImportRowDto(int RowNumber, ImportRowStatus Status, ImportRowDataDto Data, string? Error)
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

/// <summary>Wire shape `{ importId, rows: [...] }` shared by upload and GET (contracts/rest-api.md §Entry Import).</summary>
public sealed record ImportBatchDto(Guid ImportId, IReadOnlyList<ImportRowDto> Rows)
{
    public static ImportBatchDto FromEntity(ImportBatch batch) => new(
        batch.Id,
        batch.Rows.OrderBy(row => row.RowNumber).Select(ImportRowDto.FromEntity).ToList());
}
