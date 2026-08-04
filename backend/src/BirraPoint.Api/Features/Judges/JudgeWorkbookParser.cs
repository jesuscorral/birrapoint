using System.Text.RegularExpressions;
using BirraPoint.Api.Common.Errors;
using ClosedXML.Excel;

namespace BirraPoint.Api.Features.Judges;

/// <summary>One parsed data row, before any staging entity exists (T114/US14).</summary>
public sealed record ParsedJudgeImportRow(
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
/// Features/Import/WorkbookParser, no cell resolves against any catalog — BJCP rank, BJCP ID, and
/// preferred category are all stored verbatim as free text; only Name/Email presence and
/// well-formedness determine Valid/Invalid (FR-056). File-level problems (not `.xlsx`, no
/// worksheet, missing required columns, zero data rows) throw <see cref="DomainException"/> with
/// <see cref="DomainErrorType.InvalidImportFile"/> — the caller does not need to catch anything
/// else.
/// </summary>
public static class JudgeWorkbookParser
{
    private static readonly string[] RequiredHeaders =
    [
        "Nombre y apellidos",
        "Correo electrónico",
    ];

    internal static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public static IReadOnlyList<ParsedJudgeImportRow> Parse(Stream fileStream)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(fileStream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new DomainException(DomainErrorType.InvalidImportFile, "The uploaded file is not a readable .xlsx workbook.");
        }

        using (workbook)
        {
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet is null)
            {
                throw new DomainException(DomainErrorType.InvalidImportFile, "The workbook has no worksheets.");
            }

            var columnIndex = ReadHeaderColumns(worksheet.Row(1));

            var missingHeaders = RequiredHeaders.Where(header => !columnIndex.ContainsKey(header)).ToList();
            if (missingHeaders.Count > 0)
            {
                throw new DomainException(
                    DomainErrorType.InvalidImportFile,
                    $"Missing required column(s): {string.Join(", ", missingHeaders)}.");
            }

            var nameCol = columnIndex["Nombre y apellidos"];
            var emailCol = columnIndex["Correo electrónico"];
            var bjcpRankCol = columnIndex.GetValueOrDefault("Rango BJCP");
            var bjcpIdCol = columnIndex.GetValueOrDefault("BJCP ID");
            var preferredCategoryCol = columnIndex.GetValueOrDefault("Categoría preferida");
            var preferencesCol = columnIndex.GetValueOrDefault("Preferencias");

            var lastRowUsed = worksheet.LastRowUsed()?.RowNumber() ?? 1;
            var rows = new List<ParsedJudgeImportRow>();
            var rowNumber = 0;

            for (var excelRow = 2; excelRow <= lastRowUsed; excelRow++)
            {
                var row = worksheet.Row(excelRow);
                if (row.IsEmpty())
                {
                    break;
                }

                rowNumber++;
                rows.Add(ParseRow(
                    rowNumber,
                    ReadText(row, nameCol),
                    ReadText(row, emailCol),
                    ReadText(row, bjcpRankCol),
                    ReadText(row, bjcpIdCol),
                    ReadText(row, preferredCategoryCol),
                    ReadText(row, preferencesCol)));
            }

            if (rows.Count == 0)
            {
                throw new DomainException(DomainErrorType.InvalidImportFile, "The workbook has no data rows.");
            }

            return rows;
        }
    }

    private static Dictionary<string, int> ReadHeaderColumns(IXLRow headerRow)
    {
        var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastHeaderColumn = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;

        for (var col = 1; col <= lastHeaderColumn; col++)
        {
            var header = headerRow.Cell(col).GetString().Trim();
            if (header.Length > 0 && !columnIndex.ContainsKey(header))
            {
                columnIndex[header] = col;
            }
        }

        return columnIndex;
    }

    private static string? ReadText(IXLRow row, int columnIndex)
    {
        if (columnIndex <= 0)
        {
            return null;
        }

        var value = row.Cell(columnIndex).GetString().Trim();
        return value.Length == 0 ? null : value;
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
        else if (email.Length > 320 || !EmailPattern.IsMatch(email))
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
