namespace BirraPoint.Api.Features.Import.CommonComponents;

/// <summary>One parsed data row, before any staging entity exists.</summary>
internal sealed record ParsedBeerImportRow(
    int RowNumber,
    string? ParticipantName,
    string? ParticipantEmail,
    string? AcceMemberNumber,
    DateOnly? DateOfBirth,
    string? Phone,
    string? CategoryText,
    Guid? ResolvedCompetitionCategoryId,
    string? StyleText,
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
    BeerImportRowStatus Status,
    string? ErrorMessage);
