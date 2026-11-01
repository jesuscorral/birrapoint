namespace BirraPoint.Api.Features.Import.Features.EditJudgeImportRow;

/// <summary>Full-replace editable row shape (contracts/judge-import-file.md §Full row edit).</summary>
internal sealed record EditJudgeImportRowRequest(
    string? Name,
    string? Email,
    string? BjcpRank,
    string? BjcpId,
    string? PreferredCategory,
    string? Preferences);
