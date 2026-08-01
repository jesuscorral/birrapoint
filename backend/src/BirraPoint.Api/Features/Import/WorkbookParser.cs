using System.Globalization;
using System.Text.RegularExpressions;
using BirraPoint.Api.Common.Errors;
using ClosedXML.Excel;

namespace BirraPoint.Api.Features.Import;

/// <summary>Lightweight catalog projection used for style matching, avoiding a DB dependency in the parser itself.</summary>
public sealed record StyleCatalogEntry(string Code, string Name);

/// <summary>Lightweight projection of this competition's own organizer-defined categories (wizard step 3).</summary>
public sealed record CategoryCatalogEntry(Guid Id, string Name);

/// <summary>One allowed (category, style) pairing under this competition's FR-052 configuration —
/// projected from <see cref="BirraPoint.Api.Domain.CompetitionCategoryStyle"/>, used for the
/// FR-053 cross-check. Public (not internal) because it's a parameter type of the public
/// <see cref="WorkbookParser.Parse"/> method, same as <see cref="StyleCatalogEntry"/>/
/// <see cref="CategoryCatalogEntry"/>.</summary>
public sealed record CategoryStyleCatalogEntry(Guid CategoryId, string StyleCode);

/// <summary>One parsed data row, before any staging entity exists (T031/T034).</summary>
public sealed record ParsedImportRow(
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

/// <summary>
/// Implements contracts/import-file.md: first worksheet only, row 1 is the header (case
/// -insensitive/trimmed, order-independent), parsing stops at the first fully empty row. Style is
/// matched by splitting the cell on the first ". " and checking the prefix against the catalog
/// code, falling back to an exact code-or-name match against the whole cell. Category is matched
/// by an exact (case-insensitive, trimmed) name against this competition's own CompetitionCategory
/// rows. File-level problems (not `.xlsx`, no worksheet, missing required columns, zero data rows)
/// throw <see cref="DomainException"/> with <see cref="DomainErrorType.InvalidImportFile"/> — the
/// caller does not need to catch anything else.
/// </summary>
public static class WorkbookParser
{
    private static readonly string[] RequiredHeaders =
    [
        "Marca temporal",
        "Dirección de correo electrónico",
        "Nombre y apellidos",
        "Categoria",
        "Estilo",
        "Grado alcohol: (%)",
    ];

    internal static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private const decimal MaxAbvPercent = 99.99m;

    public static IReadOnlyList<ParsedImportRow> Parse(
        Stream fileStream,
        IReadOnlyCollection<StyleCatalogEntry> styles,
        IReadOnlyCollection<CategoryCatalogEntry> categories,
        IReadOnlyCollection<CategoryStyleCatalogEntry> allowedPairs)
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

            var submittedAtCol = columnIndex["Marca temporal"];
            var emailCol = columnIndex["Dirección de correo electrónico"];
            var nameCol = columnIndex["Nombre y apellidos"];
            var categoryCol = columnIndex["Categoria"];
            var styleCol = columnIndex["Estilo"];
            var abvCol = columnIndex["Grado alcohol: (%)"];

            // "Número de botellas enviadas" is intentionally never looked up — ignored entirely,
            // per import-file.md.
            var acceMemberNumberCol = columnIndex.GetValueOrDefault("Numero socio ACCE");
            var dateOfBirthCol = columnIndex.GetValueOrDefault("Fecha de nacimiento");
            var phoneCol = columnIndex.GetValueOrDefault("Teléfono");
            var brewDateCol = columnIndex.GetValueOrDefault("Fecha de elaboración");
            var bottlingDateCol = columnIndex.GetValueOrDefault("Fecha de embotellado");
            var maltsCol = columnIndex.GetValueOrDefault("Maltas utilizadas");
            var hopsCol = columnIndex.GetValueOrDefault("Lupulos utilizados");
            var yeastCol = columnIndex.GetValueOrDefault("Levadura utilizada");
            var otherIngredientsCol = columnIndex.GetValueOrDefault("Otros ingredientes");
            var entryInstructionsCol = columnIndex.GetValueOrDefault("Instrucciones de entrada");

            var lastRowUsed = worksheet.LastRowUsed()?.RowNumber() ?? 1;
            var rows = new List<ParsedImportRow>();
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
                    ReadNumericOrText(row, acceMemberNumberCol),
                    ReadDate(row, dateOfBirthCol),
                    ReadNumericOrText(row, phoneCol),
                    ReadText(row, categoryCol),
                    ReadText(row, styleCol),
                    ReadDateTime(row, submittedAtCol),
                    ReadNumber(row, abvCol),
                    ReadDate(row, brewDateCol),
                    ReadDate(row, bottlingDateCol),
                    ReadText(row, maltsCol),
                    ReadText(row, hopsCol),
                    ReadText(row, yeastCol),
                    ReadText(row, otherIngredientsCol),
                    ReadText(row, entryInstructionsCol),
                    styles,
                    categories,
                    allowedPairs));
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

    /// <summary>Numero socio ACCE/Teléfono may be typed as a genuine Excel number or as free text;
    /// either way this stores plain digits, never a decimal/scientific-notation artifact.</summary>
    private static string? ReadNumericOrText(IXLRow row, int columnIndex)
    {
        if (columnIndex <= 0)
        {
            return null;
        }

        var cell = row.Cell(columnIndex);
        if (cell.IsEmpty())
        {
            return null;
        }

        if (cell.DataType == XLDataType.Number)
        {
            var number = cell.GetDouble();
            return number == Math.Truncate(number)
                ? ((long)number).ToString(CultureInfo.InvariantCulture)
                : number.ToString(CultureInfo.InvariantCulture);
        }

        var text = cell.GetString().Trim();
        return text.Length == 0 ? null : text;
    }

    private readonly record struct DateCell(DateOnly? Value, bool IsPresent, bool IsError);

    private static DateCell ReadDate(IXLRow row, int columnIndex)
    {
        if (columnIndex <= 0)
        {
            return new DateCell(null, false, false);
        }

        var cell = row.Cell(columnIndex);
        if (cell.IsEmpty())
        {
            return new DateCell(null, false, false);
        }

        if (cell.DataType == XLDataType.DateTime)
        {
            return new DateCell(DateOnly.FromDateTime(cell.GetDateTime()), true, false);
        }

        var text = cell.GetString().Trim();
        if (text.Length == 0)
        {
            return new DateCell(null, false, false);
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateCell(DateOnly.FromDateTime(parsed), true, false)
            : new DateCell(null, true, true);
    }

    private readonly record struct DateTimeCell(DateTime? Value, bool IsPresent, bool IsError);

    private static DateTimeCell ReadDateTime(IXLRow row, int columnIndex)
    {
        if (columnIndex <= 0)
        {
            return new DateTimeCell(null, false, false);
        }

        var cell = row.Cell(columnIndex);
        if (cell.IsEmpty())
        {
            return new DateTimeCell(null, false, false);
        }

        if (cell.DataType == XLDataType.DateTime)
        {
            return new DateTimeCell(cell.GetDateTime(), true, false);
        }

        var text = cell.GetString().Trim();
        if (text.Length == 0)
        {
            return new DateTimeCell(null, false, false);
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeCell(parsed, true, false)
            : new DateTimeCell(null, true, true);
    }

    private readonly record struct NumberCell(decimal? Value, bool IsPresent, bool IsError);

    private static NumberCell ReadNumber(IXLRow row, int columnIndex)
    {
        if (columnIndex <= 0)
        {
            return new NumberCell(null, false, false);
        }

        var cell = row.Cell(columnIndex);
        if (cell.IsEmpty())
        {
            return new NumberCell(null, false, false);
        }

        if (cell.DataType == XLDataType.Number)
        {
            return new NumberCell((decimal)cell.GetDouble(), true, false);
        }

        var text = cell.GetString().Trim();
        if (text.Length == 0)
        {
            return new NumberCell(null, false, false);
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? new NumberCell(parsed, true, false)
            : new NumberCell(null, true, true);
    }

    private static ParsedImportRow ParseRow(
        int rowNumber,
        string? participantName,
        string? participantEmail,
        string? acceMemberNumber,
        DateCell dateOfBirth,
        string? phone,
        string? categoryText,
        string? styleText,
        DateTimeCell submittedAt,
        NumberCell abv,
        DateCell brewDate,
        DateCell bottlingDate,
        string? malts,
        string? hops,
        string? yeast,
        string? otherIngredients,
        string? entryInstructions,
        IReadOnlyCollection<StyleCatalogEntry> styles,
        IReadOnlyCollection<CategoryCatalogEntry> categories,
        IReadOnlyCollection<CategoryStyleCatalogEntry> allowedPairs)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(participantName))
        {
            errors.Add("Nombre y apellidos is required.");
        }
        else if (participantName.Length > 200)
        {
            errors.Add("Nombre y apellidos exceeds 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(participantEmail))
        {
            errors.Add("Dirección de correo electrónico is required.");
        }
        else if (participantEmail.Length > 320 || !EmailPattern.IsMatch(participantEmail))
        {
            errors.Add("Dirección de correo electrónico is not a valid email address.");
        }

        if (acceMemberNumber is { Length: > 50 })
        {
            errors.Add("Numero socio ACCE exceeds 50 characters.");
        }

        if (dateOfBirth.IsError)
        {
            errors.Add("Fecha de nacimiento is not a valid date.");
        }

        if (phone is { Length: > 30 })
        {
            errors.Add("Teléfono exceeds 30 characters.");
        }

        if (string.IsNullOrWhiteSpace(categoryText))
        {
            errors.Add("Categoria is required.");
        }

        if (string.IsNullOrWhiteSpace(styleText))
        {
            errors.Add("Estilo is required.");
        }

        if (!submittedAt.IsPresent)
        {
            errors.Add("Marca temporal is required.");
        }
        else if (submittedAt.IsError)
        {
            errors.Add("Marca temporal is not a valid date/time.");
        }

        if (!abv.IsPresent)
        {
            errors.Add("Grado alcohol: (%) is required.");
        }
        else if (abv.IsError)
        {
            errors.Add("Grado alcohol: (%) is not a valid number.");
        }
        else if (abv.Value is < 0 or > MaxAbvPercent)
        {
            errors.Add($"Grado alcohol: (%) must be between 0 and {MaxAbvPercent}.");
        }

        if (brewDate.IsError)
        {
            errors.Add("Fecha de elaboración is not a valid date.");
        }

        if (bottlingDate.IsError)
        {
            errors.Add("Fecha de embotellado is not a valid date.");
        }

        if (malts is { Length: > 1000 })
        {
            errors.Add("Maltas utilizadas exceeds 1000 characters.");
        }

        if (hops is { Length: > 1000 })
        {
            errors.Add("Lupulos utilizados exceeds 1000 characters.");
        }

        if (yeast is { Length: > 1000 })
        {
            errors.Add("Levadura utilizada exceeds 1000 characters.");
        }

        if (otherIngredients is { Length: > 1000 })
        {
            errors.Add("Otros ingredientes exceeds 1000 characters.");
        }

        if (entryInstructions is { Length: > 1000 })
        {
            errors.Add("Instrucciones de entrada exceeds 1000 characters.");
        }

        var submittedAtOffset = ToDateTimeOffset(submittedAt.Value);

        if (errors.Count > 0)
        {
            return new ParsedImportRow(
                rowNumber, participantName, participantEmail, acceMemberNumber, dateOfBirth.Value, phone,
                categoryText, ResolvedCompetitionCategoryId: null, styleText, ResolvedStyleCode: null,
                submittedAtOffset, abv.Value, brewDate.Value, bottlingDate.Value,
                malts, hops, yeast, otherIngredients, entryInstructions,
                ImportRowStatus.Invalid, string.Join(" ", errors));
        }

        var matchedCategoryId = MatchCategory(categoryText, categories);
        if (matchedCategoryId is null)
        {
            return new ParsedImportRow(
                rowNumber, participantName, participantEmail, acceMemberNumber, dateOfBirth.Value, phone,
                categoryText, null, styleText, null,
                submittedAtOffset, abv.Value, brewDate.Value, bottlingDate.Value,
                malts, hops, yeast, otherIngredients, entryInstructions,
                ImportRowStatus.CategoryMismatch,
                $"Categoria '{categoryText}' does not match any category configured for this competition.");
        }

        var matchedStyleCode = MatchStyleCode(styleText, styles);
        if (matchedStyleCode is null)
        {
            return new ParsedImportRow(
                rowNumber, participantName, participantEmail, acceMemberNumber, dateOfBirth.Value, phone,
                categoryText, matchedCategoryId, styleText, null,
                submittedAtOffset, abv.Value, brewDate.Value, bottlingDate.Value,
                malts, hops, yeast, otherIngredients, entryInstructions,
                ImportRowStatus.StyleMismatch,
                $"Estilo '{styleText}' does not match any BJCP 2021 catalog code or name.");
        }

        // FR-053: both resolved individually — still cross-check the pair against this
        // competition's own category/style allow-list (CompetitionCategoryStyle).
        var isAllowedPair = allowedPairs.Any(pair =>
            pair.CategoryId == matchedCategoryId.Value
            && string.Equals(pair.StyleCode, matchedStyleCode, StringComparison.OrdinalIgnoreCase));
        if (!isAllowedPair)
        {
            var matchedCategoryName = categories.First(category => category.Id == matchedCategoryId.Value).Name;
            return new ParsedImportRow(
                rowNumber, participantName, participantEmail, acceMemberNumber, dateOfBirth.Value, phone,
                categoryText, matchedCategoryId, styleText, matchedStyleCode,
                submittedAtOffset, abv.Value, brewDate.Value, bottlingDate.Value,
                malts, hops, yeast, otherIngredients, entryInstructions,
                ImportRowStatus.CategoryStyleMismatch,
                $"Estilo '{styleText}' ({matchedStyleCode}) is a valid BJCP style, but is not assigned to category '{matchedCategoryName}' in this competition.");
        }

        return new ParsedImportRow(
            rowNumber, participantName, participantEmail, acceMemberNumber, dateOfBirth.Value, phone,
            categoryText, matchedCategoryId, styleText, matchedStyleCode,
            submittedAtOffset, abv.Value, brewDate.Value, bottlingDate.Value,
            malts, hops, yeast, otherIngredients, entryInstructions,
            ImportRowStatus.Valid, ErrorMessage: null);
    }

    // Excel cells carry no timezone — treated as UTC for a deterministic, reproducible SubmittedAt.
    private static DateTimeOffset? ToDateTimeOffset(DateTime? value) =>
        value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

    /// <summary>Internal (not private): reused by RevalidateImport (FR-054) — same assembly, no
    /// need to duplicate the matching logic there.</summary>
    internal static Guid? MatchCategory(string? categoryText, IReadOnlyCollection<CategoryCatalogEntry> categories)
    {
        if (string.IsNullOrWhiteSpace(categoryText))
        {
            return null;
        }

        var trimmed = categoryText.Trim();
        return categories
            .FirstOrDefault(category => string.Equals(category.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    /// <summary>Internal (not private): reused by RevalidateImport (FR-054) — same assembly, no
    /// need to duplicate the matching logic there.</summary>
    internal static string? MatchStyleCode(string? styleText, IReadOnlyCollection<StyleCatalogEntry> styles)
    {
        if (string.IsNullOrWhiteSpace(styleText))
        {
            return null;
        }

        var separatorIndex = styleText.IndexOf(". ", StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            var prefix = styleText[..separatorIndex];
            var prefixMatch = styles.FirstOrDefault(style => string.Equals(style.Code, prefix, StringComparison.OrdinalIgnoreCase));
            if (prefixMatch is not null)
            {
                return prefixMatch.Code;
            }
        }

        var wholeMatch = styles.FirstOrDefault(style =>
            string.Equals(style.Code, styleText, StringComparison.OrdinalIgnoreCase)
            || string.Equals(style.Name, styleText, StringComparison.OrdinalIgnoreCase));

        return wholeMatch?.Code;
    }
}
