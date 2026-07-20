using System.Text.RegularExpressions;
using BirraPoint.Api.Common.Errors;
using ClosedXML.Excel;

namespace BirraPoint.Api.Features.Import;

/// <summary>Lightweight catalog projection used for style matching, avoiding a DB dependency in the parser itself.</summary>
public sealed record StyleCatalogEntry(string Code, string Name);

/// <summary>One parsed data row, before any staging entity exists (T031/T034).</summary>
public sealed record ParsedImportRow(
    int RowNumber,
    string? ParticipantName,
    string? ParticipantEmail,
    string? BeerName,
    string? StyleText,
    IReadOnlyList<string> Collaborators,
    string? ResolvedStyleCode,
    ImportRowStatus Status,
    string? ErrorMessage);

/// <summary>
/// Implements contracts/import-file.md: first worksheet only, row 1 is the header (case
/// -insensitive/trimmed, order-independent), parsing stops at the first fully empty row. Style is
/// matched by exact BJCP code or exact name (case-insensitive). File-level problems (not `.xlsx`,
/// no worksheet, missing required columns, zero data rows) throw <see cref="DomainException"/>
/// with <see cref="DomainErrorType.InvalidImportFile"/> — the caller does not need to catch
/// anything else.
/// </summary>
public static class WorkbookParser
{
    private static readonly string[] RequiredHeaders = ["ParticipantName", "ParticipantEmail", "BeerName", "Style"];

    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public static IReadOnlyList<ParsedImportRow> Parse(Stream fileStream, IReadOnlyCollection<StyleCatalogEntry> styles)
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

            var participantNameCol = columnIndex["ParticipantName"];
            var participantEmailCol = columnIndex["ParticipantEmail"];
            var beerNameCol = columnIndex["BeerName"];
            var styleCol = columnIndex["Style"];
            var collaboratorsCol = columnIndex.GetValueOrDefault("Collaborators");

            var lastRowUsed = worksheet.LastRowUsed()?.RowNumber() ?? 1;
            var rows = new List<ParsedImportRow>();
            var seenPairs = new HashSet<(string Email, string BeerName)>();
            var rowNumber = 0;

            for (var excelRow = 2; excelRow <= lastRowUsed; excelRow++)
            {
                var row = worksheet.Row(excelRow);

                var participantName = GetCell(row, participantNameCol);
                var participantEmail = GetCell(row, participantEmailCol);
                var beerName = GetCell(row, beerNameCol);
                var styleText = GetCell(row, styleCol);
                var collaboratorsRaw = GetCell(row, collaboratorsCol);

                if (participantName is null && participantEmail is null && beerName is null
                    && styleText is null && collaboratorsRaw is null)
                {
                    break;
                }

                rowNumber++;
                rows.Add(ParseRow(rowNumber, participantName, participantEmail, beerName, styleText, collaboratorsRaw, styles, seenPairs));
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

    private static string? GetCell(IXLRow row, int columnIndex)
    {
        if (columnIndex <= 0)
        {
            return null;
        }

        var value = row.Cell(columnIndex).GetString().Trim();
        return value.Length == 0 ? null : value;
    }

    private static ParsedImportRow ParseRow(
        int rowNumber,
        string? participantName,
        string? participantEmail,
        string? beerName,
        string? styleText,
        string? collaboratorsRaw,
        IReadOnlyCollection<StyleCatalogEntry> styles,
        HashSet<(string Email, string BeerName)> seenPairs)
    {
        var errors = new List<string>();
        var collaborators = new List<string>();

        if (string.IsNullOrWhiteSpace(participantName))
        {
            errors.Add("ParticipantName is required.");
        }
        else if (participantName.Length > 200)
        {
            errors.Add("ParticipantName exceeds 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(participantEmail))
        {
            errors.Add("ParticipantEmail is required.");
        }
        else if (participantEmail.Length > 320 || !EmailPattern.IsMatch(participantEmail))
        {
            errors.Add("ParticipantEmail is not a valid email address.");
        }

        if (string.IsNullOrWhiteSpace(beerName))
        {
            errors.Add("BeerName is required.");
        }
        else if (beerName.Length > 200)
        {
            errors.Add("BeerName exceeds 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(styleText))
        {
            errors.Add("Style is required.");
        }

        if (!string.IsNullOrWhiteSpace(collaboratorsRaw))
        {
            foreach (var token in collaboratorsRaw.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!EmailPattern.IsMatch(token))
                {
                    errors.Add($"Collaborator email '{token}' is not valid.");
                    continue;
                }

                collaborators.Add(token);
            }
        }

        if (errors.Count == 0 && participantEmail is not null && beerName is not null)
        {
            var pairKey = (participantEmail.Trim().ToLowerInvariant(), beerName.Trim().ToLowerInvariant());
            if (!seenPairs.Add(pairKey))
            {
                errors.Add("Duplicate ParticipantEmail/BeerName pair within this file.");
            }
        }

        if (errors.Count > 0)
        {
            return new ParsedImportRow(
                rowNumber, participantName, participantEmail, beerName, styleText, collaborators,
                ResolvedStyleCode: null, ImportRowStatus.Invalid, string.Join(" ", errors));
        }

        var matchedStyle = styles.FirstOrDefault(style =>
            string.Equals(style.Code, styleText, StringComparison.OrdinalIgnoreCase)
            || string.Equals(style.Name, styleText, StringComparison.OrdinalIgnoreCase));

        if (matchedStyle is null)
        {
            return new ParsedImportRow(
                rowNumber, participantName, participantEmail, beerName, styleText, collaborators,
                ResolvedStyleCode: null, ImportRowStatus.StyleMismatch,
                $"Style '{styleText}' does not match any BJCP 2021 catalog code or name.");
        }

        return new ParsedImportRow(
            rowNumber, participantName, participantEmail, beerName, styleText, collaborators,
            matchedStyle.Code, ImportRowStatus.Valid, ErrorMessage: null);
    }
}
