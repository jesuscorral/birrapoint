namespace BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents.Dtos;

/// <summary>Round-trips every editable/raw-parsed cell value, for the judge-roster Mapping &amp;
/// Correction screen (contracts/rest-api.md §Judge Roster Import).</summary>
internal sealed record JudgeImportRowDataDto(
    string? Name,
    string? Email,
    string? BjcpRank,
    string? BjcpId,
    string? PreferredCategory,
    string? Preferences);
