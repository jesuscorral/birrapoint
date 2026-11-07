using BirraPoint.Api.Modules.Organization.Errors;
using BuildingBlocks.Spreadsheet;

namespace BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents;

/// <summary>
/// Implements contracts/import-file.md: first worksheet only, row 1 is the header (case
/// -insensitive/trimmed, order-independent), parsing stops at the first fully empty row. Style is
/// matched by splitting the cell on the first ". " and checking the prefix against the catalog
/// code, falling back to an exact code-or-name match against the whole cell. Category is matched
/// by an exact (case-insensitive, trimmed) name against this competition's own CompetitionCategory
/// rows. File-level problems (not `.xlsx`, no worksheet, missing required columns, zero data rows)
/// throw <see cref="OrganizationException"/> with <see cref="OrganizationError.InvalidImportFile"/> — the
/// caller does not need to catch anything else.
/// </summary>
internal static class BeerWorkbookParser
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

    private const decimal MaxAbvPercent = 99.99m;

    public static IReadOnlyList<ParsedBeerImportRow> Parse(
        Stream fileStream,
        IReadOnlyCollection<StyleCatalogEntry> styles,
        IReadOnlyCollection<CategoryCatalogEntry> categories,
        IReadOnlyCollection<CategoryStyleCatalogEntry> allowedPairs)
    {
        try
        {
            using var reader = SpreadsheetReader.Open(fileStream);
            reader.RequireHeaders(RequiredHeaders);

            var rows = new List<ParsedBeerImportRow>();
            var rowNumber = 0;

            foreach (var row in reader.ReadRows())
            {
                rowNumber++;
                // "Número de botellas enviadas" is intentionally never read — ignored entirely,
                // per import-file.md.
                rows.Add(ParseRow(
                    rowNumber,
                    row.ReadText("Nombre y apellidos"),
                    row.ReadText("Dirección de correo electrónico"),
                    row.ReadNumericOrText("Numero socio ACCE"),
                    row.ReadDate("Fecha de nacimiento"),
                    row.ReadNumericOrText("Teléfono"),
                    row.ReadText("Categoria"),
                    row.ReadText("Estilo"),
                    row.ReadDateTime("Marca temporal"),
                    row.ReadNumber("Grado alcohol: (%)"),
                    row.ReadDate("Fecha de elaboración"),
                    row.ReadDate("Fecha de embotellado"),
                    row.ReadText("Maltas utilizadas"),
                    row.ReadText("Lupulos utilizados"),
                    row.ReadText("Levadura utilizada"),
                    row.ReadText("Otros ingredientes"),
                    row.ReadText("Instrucciones de entrada"),
                    styles,
                    categories,
                    allowedPairs));
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

    private static ParsedBeerImportRow ParseRow(
        int rowNumber,
        string? participantName,
        string? participantEmail,
        string? acceMemberNumber,
        CellValue<DateOnly> dateOfBirth,
        string? phone,
        string? categoryText,
        string? styleText,
        CellValue<DateTime> submittedAt,
        CellValue<decimal> abv,
        CellValue<DateOnly> brewDate,
        CellValue<DateOnly> bottlingDate,
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
        else if (participantEmail.Length > ImportEmail.MaxLength || !ImportEmail.IsWellFormed(participantEmail))
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
            return new ParsedBeerImportRow(
                rowNumber, participantName, participantEmail, acceMemberNumber, dateOfBirth.Value, phone,
                categoryText, ResolvedCompetitionCategoryId: null, styleText, ResolvedStyleCode: null,
                submittedAtOffset, abv.Value, brewDate.Value, bottlingDate.Value,
                malts, hops, yeast, otherIngredients, entryInstructions,
                BeerImportRowStatus.Invalid, string.Join(" ", errors));
        }

        var matchedCategoryId = MatchCategory(categoryText, categories);
        if (matchedCategoryId is null)
        {
            return new ParsedBeerImportRow(
                rowNumber, participantName, participantEmail, acceMemberNumber, dateOfBirth.Value, phone,
                categoryText, null, styleText, null,
                submittedAtOffset, abv.Value, brewDate.Value, bottlingDate.Value,
                malts, hops, yeast, otherIngredients, entryInstructions,
                BeerImportRowStatus.CategoryMismatch,
                $"Categoria '{categoryText}' does not match any category configured for this competition.");
        }

        var matchedStyleCode = MatchStyleCode(styleText, styles);
        if (matchedStyleCode is null)
        {
            return new ParsedBeerImportRow(
                rowNumber, participantName, participantEmail, acceMemberNumber, dateOfBirth.Value, phone,
                categoryText, matchedCategoryId, styleText, null,
                submittedAtOffset, abv.Value, brewDate.Value, bottlingDate.Value,
                malts, hops, yeast, otherIngredients, entryInstructions,
                BeerImportRowStatus.StyleMismatch,
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
            return new ParsedBeerImportRow(
                rowNumber, participantName, participantEmail, acceMemberNumber, dateOfBirth.Value, phone,
                categoryText, matchedCategoryId, styleText, matchedStyleCode,
                submittedAtOffset, abv.Value, brewDate.Value, bottlingDate.Value,
                malts, hops, yeast, otherIngredients, entryInstructions,
                BeerImportRowStatus.CategoryStyleMismatch,
                $"Estilo '{styleText}' ({matchedStyleCode}) is a valid BJCP style, but is not assigned to category '{matchedCategoryName}' in this competition.");
        }

        return new ParsedBeerImportRow(
            rowNumber, participantName, participantEmail, acceMemberNumber, dateOfBirth.Value, phone,
            categoryText, matchedCategoryId, styleText, matchedStyleCode,
            submittedAtOffset, abv.Value, brewDate.Value, bottlingDate.Value,
            malts, hops, yeast, otherIngredients, entryInstructions,
            BeerImportRowStatus.Valid, ErrorMessage: null);
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
