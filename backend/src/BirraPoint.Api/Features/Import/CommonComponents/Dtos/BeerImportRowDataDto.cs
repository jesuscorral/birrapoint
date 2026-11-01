namespace BirraPoint.Api.Features.Import.CommonComponents.Dtos;

/// <summary>Round-trips every editable/raw-parsed cell value, for the Mapping &amp; Correction
/// screen (contracts/rest-api.md §Entry Import). <see cref="Category"/>/<see cref="Style"/> are
/// the raw parsed cell text (may not resolve to anything); <see cref="CompetitionCategoryId"/>/
/// <see cref="ResolvedStyleCode"/> are the resolved references, set at parse time or by the
/// organizer via the full row edit.</summary>
internal sealed record BeerImportRowDataDto(
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
