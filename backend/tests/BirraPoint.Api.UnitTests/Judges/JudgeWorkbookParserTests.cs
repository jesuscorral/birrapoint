using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Features.Judges;
using ClosedXML.Excel;

namespace BirraPoint.Api.UnitTests.Judges;

/// <summary>
/// T110: pure parsing tests for <see cref="JudgeWorkbookParser"/> against
/// contracts/judge-import-file.md — the organizer's own club roster export: header matching,
/// Valid/Invalid row outcomes (missing/malformed name or email), verbatim free-text fields
/// (including the literal `&lt;br&gt;`-style text R-20 calls out), and duplicate-email-within-file
/// handling (the parser itself never dedupes — that happens at consolidation, FR-058).
/// </summary>
public sealed class JudgeWorkbookParserTests
{
    private static readonly string[] StandardHeaders =
    [
        "Nombre y apellidos",
        "Correo electrónico",
        "Rango BJCP",
        "BJCP ID",
        "Categoría preferida",
        "Preferencias",
    ];

    private static byte[] BuildWorkbook(string[] headers, IEnumerable<object?[]> rows, bool includeHeaderRow = true)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Judges");

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
            case string s:
                cell.Value = s;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported test cell value type.");
        }
    }

    /// <summary>Builds one judge-roster row in standard column order, from the contract's own
    /// worked example, defaulting every field to a well-formed value.</summary>
    private static object?[] Row(
        string? name = "Ana García Ruiz",
        string? email = "ana.garcia@example.com",
        string? bjcpRank = "Certificado",
        string? bjcpId = "E4612",
        string? preferredCategory = "Estilos Clásicos",
        string? preferences = null) =>
    [
        name, email, bjcpRank, bjcpId, preferredCategory, preferences,
    ];

    private static byte[] BuildJudgeWorkbook(params object?[][] rows) => BuildWorkbook(StandardHeaders, rows);

    private static IReadOnlyList<ParsedJudgeImportRow> Parse(byte[] xlsxBytes)
    {
        using var stream = new MemoryStream(xlsxBytes);
        return JudgeWorkbookParser.Parse(stream);
    }

    [Fact]
    public void ParsesTheContractWorkedExampleAsValidWithEveryFieldPopulated()
    {
        var xlsx = BuildJudgeWorkbook(Row());

        var rows = Parse(xlsx);

        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal(JudgeImportRowStatus.Valid, row.Status);
        Assert.Equal(1, row.RowNumber);
        Assert.Equal("Ana García Ruiz", row.Name);
        Assert.Equal("ana.garcia@example.com", row.Email);
        Assert.Equal("Certificado", row.BjcpRank);
        Assert.Equal("E4612", row.BjcpId);
        Assert.Equal("Estilos Clásicos", row.PreferredCategory);
        Assert.Null(row.Preferences);
        Assert.Null(row.ErrorMessage);
    }

    [Fact]
    public void BjcpIdPlaceholderPteIsStoredVerbatim()
    {
        var xlsx = BuildJudgeWorkbook(Row(bjcpId: "Pte"));

        var rows = Parse(xlsx);

        Assert.Equal(JudgeImportRowStatus.Valid, rows[0].Status);
        Assert.Equal("Pte", rows[0].BjcpId);
    }

    [Fact]
    public void BareNumericBjcpIdIsStoredVerbatimAsText()
    {
        var xlsx = BuildJudgeWorkbook(Row(bjcpId: "10649"));

        var rows = Parse(xlsx);

        Assert.Equal(JudgeImportRowStatus.Valid, rows[0].Status);
        Assert.Equal("10649", rows[0].BjcpId);
    }

    [Fact]
    public void LiteralBrStyleTextInPreferencesIsStoredVerbatimNeverInterpretedAsMarkup()
    {
        const string preferences =
            "Me gustaría compartir mesa con Pablo. <br>Mi pareja se ofrece para ayudar. <br>Un saludo.";
        var xlsx = BuildJudgeWorkbook(Row(preferences: preferences));

        var rows = Parse(xlsx);

        Assert.Equal(JudgeImportRowStatus.Valid, rows[0].Status);
        Assert.Equal(preferences, rows[0].Preferences);
    }

    [Fact]
    public void RowMissingNameIsMarkedInvalid()
    {
        var xlsx = BuildJudgeWorkbook(Row(name: null));

        var rows = Parse(xlsx);

        Assert.Single(rows);
        Assert.Equal(JudgeImportRowStatus.Invalid, rows[0].Status);
        Assert.Contains("Nombre y apellidos", rows[0].ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RowMissingEmailIsMarkedInvalid()
    {
        var xlsx = BuildJudgeWorkbook(Row(email: null));

        var rows = Parse(xlsx);

        Assert.Single(rows);
        Assert.Equal(JudgeImportRowStatus.Invalid, rows[0].Status);
        Assert.Contains("Correo electrónico", rows[0].ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RowWithAMalformedEmailIsMarkedInvalid()
    {
        var xlsx = BuildJudgeWorkbook(Row(email: "not-an-email"));

        var rows = Parse(xlsx);

        Assert.Single(rows);
        Assert.Equal(JudgeImportRowStatus.Invalid, rows[0].Status);
        Assert.Contains("Correo electrónico", rows[0].ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void OverLengthNameIsMarkedInvalid()
    {
        var xlsx = BuildJudgeWorkbook(Row(name: new string('A', 201)));

        var rows = Parse(xlsx);

        Assert.Equal(JudgeImportRowStatus.Invalid, rows[0].Status);
    }

    [Fact]
    public void OverLengthBjcpRankIsMarkedInvalid()
    {
        var xlsx = BuildJudgeWorkbook(Row(bjcpRank: new string('A', 101)));

        var rows = Parse(xlsx);

        Assert.Equal(JudgeImportRowStatus.Invalid, rows[0].Status);
    }

    [Fact]
    public void OverLengthBjcpIdIsMarkedInvalid()
    {
        var xlsx = BuildJudgeWorkbook(Row(bjcpId: new string('9', 51)));

        var rows = Parse(xlsx);

        Assert.Equal(JudgeImportRowStatus.Invalid, rows[0].Status);
    }

    [Fact]
    public void OverLengthPreferredCategoryIsMarkedInvalid()
    {
        var xlsx = BuildJudgeWorkbook(Row(preferredCategory: new string('A', 201)));

        var rows = Parse(xlsx);

        Assert.Equal(JudgeImportRowStatus.Invalid, rows[0].Status);
    }

    [Fact]
    public void OverLengthPreferencesIsMarkedInvalid()
    {
        var xlsx = BuildJudgeWorkbook(Row(preferences: new string('A', 2001)));

        var rows = Parse(xlsx);

        Assert.Equal(JudgeImportRowStatus.Invalid, rows[0].Status);
    }

    [Fact]
    public void OptionalColumnsMayBeEntirelyAbsentFromTheHeaderRow()
    {
        var xlsx = BuildWorkbook(
            headers: ["Nombre y apellidos", "Correo electrónico"],
            rows: [["Ana Gomez", "ana@brew.example"]]);

        var rows = Parse(xlsx);

        Assert.Equal(JudgeImportRowStatus.Valid, rows[0].Status);
        Assert.Null(rows[0].BjcpRank);
        Assert.Null(rows[0].BjcpId);
        Assert.Null(rows[0].PreferredCategory);
        Assert.Null(rows[0].Preferences);
    }

    [Fact]
    public void HeadersAreMatchedCaseInsensitivelyTrimmedAndInAnyOrder()
    {
        var xlsx = BuildWorkbook(
            headers: [" correo electrónico ", "NOMBRE Y APELLIDOS", "bjcp id", "rango bjcp"],
            rows: [["ana@brew.example", "Ana Gomez", "10649", "Certificado"]]);

        var rows = Parse(xlsx);

        Assert.Single(rows);
        Assert.Equal(JudgeImportRowStatus.Valid, rows[0].Status);
        Assert.Equal("Ana Gomez", rows[0].Name);
        Assert.Equal("ana@brew.example", rows[0].Email);
        Assert.Equal("10649", rows[0].BjcpId);
        Assert.Equal("Certificado", rows[0].BjcpRank);
    }

    [Fact]
    public void DuplicateEmailsWithinTheSameFileAreReturnedAsIndependentRowsUnmodified()
    {
        // The parser never dedupes — duplicate-email resolution to a single upsert happens at
        // consolidation (FR-058, ConsolidateJudgeImport), not at parse time.
        var xlsx = BuildJudgeWorkbook(
            Row(name: "Luis Martín Soto", email: "shared@brew.example"),
            Row(name: "Carmen López Díaz", email: "shared@brew.example"));

        var rows = Parse(xlsx);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(JudgeImportRowStatus.Valid, row.Status));
        Assert.Equal("shared@brew.example", rows[0].Email);
        Assert.Equal("shared@brew.example", rows[1].Email);
        Assert.NotEqual(rows[0].Name, rows[1].Name);
    }

    [Fact]
    public void ParsingStopsAtTheFirstFullyEmptyRow()
    {
        var xlsx = BuildJudgeWorkbook(
            Row(email: "ana@brew.example", name: "Ana Gomez"),
            new object?[6],
            Row(email: "luis@brew.example", name: "Luis Perez"));

        var rows = Parse(xlsx);

        Assert.Single(rows);
        Assert.Equal("Ana Gomez", rows[0].Name);
    }

    [Fact]
    public void MissingRequiredHeaderColumnThrowsInvalidImportFile()
    {
        var xlsx = BuildWorkbook(
            headers: ["Nombre y apellidos"],
            rows: [["Ana Gomez"]]);

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
            JudgeWorkbookParser.Parse(stream);
        });

        Assert.Equal(DomainErrorType.InvalidImportFile, exception.ErrorType);
    }
}
