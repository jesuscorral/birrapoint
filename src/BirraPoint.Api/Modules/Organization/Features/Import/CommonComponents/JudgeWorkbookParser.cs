using BirraPoint.Api.Modules.Organization.Errors;
using BuildingBlocks.Spreadsheet;

namespace BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents;

/// <summary>One parsed data row, before any staging entity exists (US14).</summary>
internal sealed record ParsedJudgeImportRow(
    int RowNumber,
    string? Name,
    string? Email,
    string? BjcpRank,
    string? BjcpId,
    string? PreferredCategory,
    string? Preferences,
    JudgeImportRowStatus Status,
    string? ErrorMessage);

/// <summary>
/// Implements contracts/judge-import-file.md: first worksheet only, row 1 is the header (case
/// -insensitive/trimmed, order-independent), parsing stops at the first fully empty row. Unlike
/// Modules/Organization/Features/Import/CommonComponents/BeerWorkbookParser, no cell resolves against any catalog — BJCP rank, BJCP ID, and
/// preferred category are all stored verbatim as free text; only Name/Email presence and
/// well-formedness determine Valid/Invalid (FR-056). File-level problems (not `.xlsx`, no
/// worksheet, missing required columns, zero data rows) throw <see cref="OrganizationException"/> with
/// <see cref="OrganizationError.InvalidImportFile"/> — the caller does not need to catch anything
/// else.
/// </summary>
internal static class JudgeWorkbookParser
{
    private static readonly string[] RequiredHeaders =
    [
        "Nombre y apellidos",
        "Correo electrónico",
    ];

    public static IReadOnlyList<ParsedJudgeImportRow> Parse(Stream fileStream)
    {
        try
        {
            using var reader = SpreadsheetReader.Open(fileStream);
            reader.RequireHeaders(RequiredHeaders);

            var rows = new List<ParsedJudgeImportRow>();
            var rowNumber = 0;

            foreach (var row in reader.ReadRows())
            {
                rowNumber++;
                rows.Add(ParseRow(
                    rowNumber,
                    row.ReadText("Nombre y apellidos"),
                    row.ReadText("Correo electrónico"),
                    row.ReadText("Rango BJCP"),
                    row.ReadText("BJCP ID"),
                    row.ReadText("Categoría preferida"),
                    row.ReadText("Preferencias")));
            }

            if (rows.Count == 0)
            {
                throw new OrganizationException(OrganizationError.InvalidImportFile, "The workbook has no data rows.");
            }

            return rows;
        }
        catch (SpreadsheetFormatException ex)
        {
            throw new OrganizationException(OrganizationError.InvalidImportFile, ex.Message);
        }
    }

    private static ParsedJudgeImportRow ParseRow(
        int rowNumber, string? name, string? email, string? bjcpRank, string? bjcpId, string? preferredCategory, string? preferences)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Nombre y apellidos is required.");
        }
        else if (name.Length > 200)
        {
            errors.Add("Nombre y apellidos exceeds 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            errors.Add("Correo electrónico is required.");
        }
        else if (email.Length > ImportEmail.MaxLength || !ImportEmail.IsWellFormed(email))
        {
            errors.Add("Correo electrónico is not a valid email address.");
        }

        if (bjcpRank is { Length: > 100 })
        {
            errors.Add("Rango BJCP exceeds 100 characters.");
        }

        if (bjcpId is { Length: > 50 })
        {
            errors.Add("BJCP ID exceeds 50 characters.");
        }

        if (preferredCategory is { Length: > 200 })
        {
            errors.Add("Categoría preferida exceeds 200 characters.");
        }

        if (preferences is { Length: > 2000 })
        {
            errors.Add("Preferencias exceeds 2000 characters.");
        }

        var status = errors.Count > 0 ? JudgeImportRowStatus.Invalid : JudgeImportRowStatus.Valid;

        return new ParsedJudgeImportRow(
            rowNumber, name, email, bjcpRank, bjcpId, preferredCategory, preferences,
            status, errors.Count > 0 ? string.Join(" ", errors) : null);
    }
}
