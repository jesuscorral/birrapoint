using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Features.Import;
using ClosedXML.Excel;

namespace BirraPoint.Api.UnitTests.Import;

/// <summary>
/// T031: pure parsing tests for <see cref="WorkbookParser"/> against contracts/import-file.md —
/// the ACCE club's Spanish-header `.xlsx` format: header matching, row status assignment
/// (Valid/StyleMismatch/CategoryMismatch/Invalid), typed date/number cell parsing, and style/
/// category matching.
/// </summary>
public sealed class WorkbookParserTests
{
    private static readonly string[] StandardHeaders =
    [
        "Marca temporal",
        "Dirección de correo electrónico",
        "Numero socio ACCE",
        "Nombre y apellidos",
        "Fecha de nacimiento",
        "Teléfono",
        "Categoria",
        "Estilo",
        "Grado alcohol: (%)",
        "Número de botellas enviadas",
        "Fecha de elaboración",
        "Fecha de embotellado",
        "Maltas utilizadas",
        "Lupulos utilizados",
        "Levadura utilizada",
        "Otros ingredientes",
        "Instrucciones de entrada",
    ];

    private static readonly Guid ClassicStylesCategoryId = Guid.NewGuid();
    private static readonly Guid SpecialtyCategoryId = Guid.NewGuid();

    private static readonly IReadOnlyCollection<StyleCatalogEntry> Styles =
    [
        new StyleCatalogEntry("21A", "American IPA"),
        new StyleCatalogEntry("21C", "Hazy IPA"),
        new StyleCatalogEntry("20C", "Imperial Stout"),
    ];

    private static readonly IReadOnlyCollection<CategoryCatalogEntry> Categories =
    [
        new CategoryCatalogEntry(ClassicStylesCategoryId, "Estilos clásicos"),
        new CategoryCatalogEntry(SpecialtyCategoryId, "Estilos de especialidad"),
    ];

    /// <summary>FR-053 allow-list: "Estilos clásicos" is assigned every style used by the default
    /// well-formed row across these tests; "Estilos de especialidad" is deliberately left with no
    /// assignments so it can exercise the CategoryStyleMismatch case.</summary>
    private static readonly IReadOnlyCollection<CategoryStyleCatalogEntry> AllowedPairs =
    [
        new CategoryStyleCatalogEntry(ClassicStylesCategoryId, "21A"),
        new CategoryStyleCatalogEntry(ClassicStylesCategoryId, "21C"),
        new CategoryStyleCatalogEntry(ClassicStylesCategoryId, "20C"),
    ];

    private static byte[] BuildWorkbook(string[] headers, IEnumerable<object?[]> rows, bool includeHeaderRow = true)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Entries");

        if (includeHeaderRow)
        {
            for (var col = 0; col < headers.Length; col++)
            {
                worksheet.Cell(1, col + 1).Value = headers[col];
            }
        }

        var rowIndex = includeHeaderRow ? 2 : 1;
        foreach (var row in rows)
        {
            for (var col = 0; col < row.Length; col++)
            {
                SetCellValue(worksheet.Cell(rowIndex, col + 1), row[col]);
            }

            rowIndex++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case DateTime dt:
                cell.Value = dt;
                break;
            case double d:
                cell.Value = d;
                break;
            case int i:
                cell.Value = (double)i;
                break;
            case string s:
                cell.Value = s;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported test cell value type.");
        }
    }

    /// <summary>Builds one ACCE row in standard column order; every parameter defaults to a
    /// well-formed value from the real organizer file's worked example so a test only needs to
    /// override the field(s) it cares about.</summary>
    private static object?[] Row(
        DateTime? submittedAt = null,
        string? email = "dezaprieto@gmail.com",
        double? acceMemberNumber = 1423.0,
        string? name = "José Deza Prieto",
        DateTime? dateOfBirth = null,
        object? phone = null,
        string? category = "Estilos clásicos",
        string? style = "21C. Hazy IPA",
        double? abv = 7.6,
        int? bottles = 3,
        DateTime? brewDate = null,
        DateTime? bottlingDate = null,
        string? malts = "Pale Ale, Trigo, Copos de avena, Melanoidin",
        string? hops = "Amarillo, Citra, Mosaic",
        string? yeast = "White Lab WL-001-P California Ale",
        string? otherIngredients = null,
        string? entryInstructions = null) =>
    [
        submittedAt ?? new DateTime(2025, 9, 1, 9, 21, 16),
        email,
        acceMemberNumber,
        name,
        dateOfBirth,
        phone ?? 699989612.0,
        category,
        style,
        abv,
        bottles,
        brewDate ?? new DateTime(2025, 8, 12),
        bottlingDate ?? new DateTime(2025, 8, 28),
        malts,
        hops,
        yeast,
        otherIngredients,
        entryInstructions,
    ];

    private static byte[] BuildAcceWorkbook(params object?[][] rows) => BuildWorkbook(StandardHeaders, rows);

    private static IReadOnlyList<ParsedImportRow> Parse(byte[] xlsxBytes)
    {
        using var stream = new MemoryStream(xlsxBytes);
        return WorkbookParser.Parse(stream, Styles, Categories, AllowedPairs);
    }

    [Fact]
    public void ParsesTheRealOrganizerWorkedExampleAsValidWithMatchedCategoryAndStyle()
    {
        var xlsx = BuildAcceWorkbook(Row());

        var rows = Parse(xlsx);

        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal(ImportRowStatus.Valid, row.Status);
        Assert.Equal(1, row.RowNumber);
        Assert.Equal("José Deza Prieto", row.ParticipantName);
        Assert.Equal("dezaprieto@gmail.com", row.ParticipantEmail);
        Assert.Equal("1423", row.AcceMemberNumber);
        Assert.Equal("699989612", row.Phone);
        Assert.Equal(ClassicStylesCategoryId, row.ResolvedCompetitionCategoryId);
        Assert.Equal("21C", row.ResolvedStyleCode);
        Assert.Equal(7.6m, row.AbvPercent);
        Assert.Equal(new DateOnly(2025, 8, 12), row.BrewDate);
        Assert.Equal(new DateOnly(2025, 8, 28), row.BottlingDate);
        Assert.Equal("Pale Ale, Trigo, Copos de avena, Melanoidin", row.Malts);
        Assert.Equal("Amarillo, Citra, Mosaic", row.Hops);
        Assert.Equal("White Lab WL-001-P California Ale", row.Yeast);
        Assert.NotNull(row.SubmittedAt);
    }

    [Fact]
    public void NumberOfBottlesColumnIsReadButNeverAppearsInTheParsedRow()
    {
        // ParsedImportRow simply has no property for it — a compile-time guarantee — so this test
        // only confirms the presence of that column/value doesn't affect parsing of anything else.
        var xlsx = BuildAcceWorkbook(Row(bottles: 12));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.Valid, rows[0].Status);
    }

    [Fact]
    public void BareStyleCodeWithoutANameSuffixStillResolves()
    {
        var xlsx = BuildAcceWorkbook(Row(style: "21C"));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.Valid, rows[0].Status);
        Assert.Equal("21C", rows[0].ResolvedStyleCode);
    }

    [Fact]
    public void BareStyleNameWithoutACodePrefixStillResolves()
    {
        var xlsx = BuildAcceWorkbook(Row(style: "American IPA"));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.Valid, rows[0].Status);
        Assert.Equal("21A", rows[0].ResolvedStyleCode);
    }

    [Fact]
    public void StyleCodePrefixMatchIsCaseInsensitive()
    {
        var xlsx = BuildAcceWorkbook(Row(style: "21c. Hazy IPA (weird casing)"));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.Valid, rows[0].Status);
        Assert.Equal("21C", rows[0].ResolvedStyleCode);
    }

    [Fact]
    public void StyleThatDoesNotMatchTheCatalogIsMarkedStyleMismatch()
    {
        var xlsx = BuildAcceWorkbook(Row(style: "99Z. Nonexistent Style"));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.StyleMismatch, rows[0].Status);
        Assert.Null(rows[0].ResolvedStyleCode);
        Assert.False(string.IsNullOrWhiteSpace(rows[0].ErrorMessage));
        // The category still resolved even though the style didn't.
        Assert.Equal(ClassicStylesCategoryId, rows[0].ResolvedCompetitionCategoryId);
    }

    [Fact]
    public void CategoryThatDoesNotMatchAnyCompetitionCategoryIsMarkedCategoryMismatch()
    {
        var xlsx = BuildAcceWorkbook(Row(category: "Estilos experimentales"));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.CategoryMismatch, rows[0].Status);
        Assert.Null(rows[0].ResolvedCompetitionCategoryId);
        Assert.False(string.IsNullOrWhiteSpace(rows[0].ErrorMessage));
        // A row with an unmatched category never gets a resolved style either, even if Estilo would
        // have matched — category is checked first.
        Assert.Null(rows[0].ResolvedStyleCode);
    }

    [Fact]
    public void CategoryMatchingIsCaseInsensitiveAndTrimsWhitespace()
    {
        var xlsx = BuildAcceWorkbook(Row(category: "  ESTILOS CLÁSICOS  "));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.Valid, rows[0].Status);
        Assert.Equal(ClassicStylesCategoryId, rows[0].ResolvedCompetitionCategoryId);
    }

    [Fact]
    public void StyleValidInTheCatalogButNotAssignedToTheResolvedCategoryIsMarkedCategoryStyleMismatch()
    {
        // "Estilos de especialidad" has no styles assigned in AllowedPairs; 21C is BJCP-valid.
        var xlsx = BuildAcceWorkbook(Row(category: "Estilos de especialidad", style: "21C. Hazy IPA"));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.CategoryStyleMismatch, rows[0].Status);
        // Both individually resolved — only the pairing is rejected.
        Assert.Equal(SpecialtyCategoryId, rows[0].ResolvedCompetitionCategoryId);
        Assert.Equal("21C", rows[0].ResolvedStyleCode);
        Assert.False(string.IsNullOrWhiteSpace(rows[0].ErrorMessage));
    }

    [Theory]
    [MemberData(nameof(MissingRequiredCellCases))]
    public void RowsWithMissingRequiredCellsAreMarkedInvalid(object?[] row)
    {
        var xlsx = BuildAcceWorkbook(row);

        var rows = Parse(xlsx);

        Assert.Single(rows);
        Assert.Equal(ImportRowStatus.Invalid, rows[0].Status);
        Assert.False(string.IsNullOrWhiteSpace(rows[0].ErrorMessage));
    }

    public static IEnumerable<object[]> MissingRequiredCellCases()
    {
        yield return [Row(email: null)];
        yield return [Row(email: "not-an-email")];
        yield return [Row(name: null)];
        yield return [Row(category: null)];
        yield return [Row(style: null)];
        yield return [Row(abv: null)];
    }

    [Fact]
    public void RowWithABlankMarcaTemporalCellIsMarkedInvalid()
    {
        var xlsx = BuildWorkbook(
            StandardHeaders,
            rows: [[null, "ana@brew.example", null, "Ana Gomez", null, null, "Estilos clásicos", "21A", 5.0, null, null, null, null, null, null, null, null]]);

        var rows = Parse(xlsx);

        Assert.Single(rows);
        Assert.Equal(ImportRowStatus.Invalid, rows[0].Status);
        Assert.Contains("Marca temporal", rows[0].ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void UnparsableMarcaTemporalTextCellIsMarkedInvalidWithAClearMessage()
    {
        var xlsx = BuildWorkbook(
            StandardHeaders,
            rows:
            [
                ["not-a-date", "ana@brew.example", null, "Ana Gomez", null, null, "Estilos clásicos", "21A", 5.0, null, null, null, null, null, null, null, null],
            ]);

        var rows = Parse(xlsx);

        Assert.Single(rows);
        Assert.Equal(ImportRowStatus.Invalid, rows[0].Status);
        Assert.Contains("Marca temporal", rows[0].ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void UnparsableOptionalDateTextCellIsMarkedInvalidWithAClearMessage()
    {
        var xlsx = BuildAcceWorkbook(Row());
        // Row() can only express typed DateTime cells — overwrite the "Fecha de nacimiento" cell
        // with an unparsable text value directly to exercise the malformed-text-cell path.
        using var workbook = new XLWorkbook(new MemoryStream(xlsx));
        var worksheet = workbook.Worksheets.First();
        worksheet.Cell(2, 5).Value = "not-a-date"; // "Fecha de nacimiento" column
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var rows = Parse(stream.ToArray());

        Assert.Single(rows);
        Assert.Equal(ImportRowStatus.Invalid, rows[0].Status);
        Assert.Contains("Fecha de nacimiento", rows[0].ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(100.0)]
    public void AbvOutsideThe0To9999RangeIsMarkedInvalid(double abv)
    {
        var xlsx = BuildAcceWorkbook(Row(abv: abv));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.Invalid, rows[0].Status);
    }

    [Fact]
    public void OverLengthParticipantNameIsMarkedInvalid()
    {
        var xlsx = BuildAcceWorkbook(Row(name: new string('A', 201)));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.Invalid, rows[0].Status);
    }

    [Fact]
    public void OverLengthAcceMemberNumberIsMarkedInvalid()
    {
        var xlsx = BuildWorkbook(
            StandardHeaders,
            rows:
            [
                [
                    new DateTime(2025, 9, 1), "ana@brew.example", null, "Ana Gomez", null, null,
                    "Estilos clásicos", "21A", 5.0, null, null, null, null, null, null, null, null,
                ],
            ]);
        // Overwrite the ACCE member number cell with an over-length text value directly (it can't
        // be expressed as a double in the Row() helper).
        using var workbook = new XLWorkbook(new MemoryStream(xlsx));
        var worksheet = workbook.Worksheets.First();
        worksheet.Cell(2, 3).Value = new string('9', 51);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var rows = Parse(stream.ToArray());

        Assert.Equal(ImportRowStatus.Invalid, rows[0].Status);
    }

    [Fact]
    public void OptionalColumnsMayBeEntirelyAbsentFromTheHeaderRow()
    {
        var xlsx = BuildWorkbook(
            headers:
            [
                "Marca temporal", "Dirección de correo electrónico", "Nombre y apellidos", "Categoria", "Estilo",
                "Grado alcohol: (%)",
            ],
            rows: [[new DateTime(2025, 9, 1), "ana@brew.example", "Ana Gomez", "Estilos clásicos", "21A", 5.0]]);

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.Valid, rows[0].Status);
        Assert.Null(rows[0].AcceMemberNumber);
        Assert.Null(rows[0].Phone);
        Assert.Null(rows[0].DateOfBirth);
        Assert.Null(rows[0].BrewDate);
        Assert.Null(rows[0].BottlingDate);
        Assert.Null(rows[0].Malts);
        Assert.Null(rows[0].EntryInstructions);
    }

    [Fact]
    public void HeadersAreMatchedCaseInsensitivelyTrimmedAndInAnyOrder()
    {
        var xlsx = BuildWorkbook(
            headers: [" estilo ", "GRADO ALCOHOL: (%)", "categoria", "nombre y apellidos", "dirección de correo electrónico", "marca temporal"],
            rows: [["21A", 5.0, "Estilos clásicos", "Ana Gomez", "ana@brew.example", new DateTime(2025, 9, 1)]]);

        var rows = Parse(xlsx);

        Assert.Single(rows);
        Assert.Equal(ImportRowStatus.Valid, rows[0].Status);
        Assert.Equal("Ana Gomez", rows[0].ParticipantName);
    }

    [Fact]
    public void ParsingStopsAtTheFirstFullyEmptyRow()
    {
        var xlsx = BuildAcceWorkbook(
            Row(email: "ana@brew.example", name: "Ana Gomez"),
            new object?[17],
            Row(email: "luis@brew.example", name: "Luis Perez"));

        var rows = Parse(xlsx);

        Assert.Single(rows);
        Assert.Equal("Ana Gomez", rows[0].ParticipantName);
    }

    [Fact]
    public void MissingRequiredHeaderColumnThrowsInvalidImportFile()
    {
        var xlsx = BuildWorkbook(
            headers: ["Marca temporal", "Dirección de correo electrónico", "Nombre y apellidos"],
            rows: [[new DateTime(2025, 9, 1), "ana@brew.example", "Ana Gomez"]]);

        var exception = Assert.Throws<DomainException>(() => Parse(xlsx));

        Assert.Equal(DomainErrorType.InvalidImportFile, exception.ErrorType);
    }

    [Fact]
    public void ZeroDataRowsThrowsInvalidImportFile()
    {
        var xlsx = BuildWorkbook(StandardHeaders, rows: []);

        var exception = Assert.Throws<DomainException>(() => Parse(xlsx));

        Assert.Equal(DomainErrorType.InvalidImportFile, exception.ErrorType);
    }

    [Fact]
    public void UnreadableBytesThrowsInvalidImportFile()
    {
        var corrupt = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        var exception = Assert.Throws<DomainException>(() =>
        {
            using var stream = new MemoryStream(corrupt);
            WorkbookParser.Parse(stream, Styles, Categories, AllowedPairs);
        });

        Assert.Equal(DomainErrorType.InvalidImportFile, exception.ErrorType);
    }
}
