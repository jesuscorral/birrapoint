namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>BR-01/FR-019: the anonymous view of an entry that tasting tables may see. It physically lacks
/// entrant fields (beer name, participant, collaborators, recipe); a contract test pins the property set.
/// <see cref="NotValidForBos"/> and <see cref="CompetitionCategoryName"/> feed organizer table views only.</summary>
internal sealed record EntrySampleInfo(
    Guid Id,
    Guid CompetitionId,
    string BlindCode,
    string StyleCode,
    decimal AbvPercent,
    string? EntryInstructions,
    bool NotValidForBos,
    string? CompetitionCategoryName);
