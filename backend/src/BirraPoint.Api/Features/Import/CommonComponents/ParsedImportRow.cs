namespace BirraPoint.Api.Features.Import.CommonComponents;

/// <summary>One parsed data row, before any staging entity exists (T031/T034).</summary>
internal sealed record ParsedImportRow(
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
    ImportRowStatus Status,
    string? ErrorMessage);
