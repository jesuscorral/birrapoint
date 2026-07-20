using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Features.Import;
using ClosedXML.Excel;

namespace BirraPoint.Api.UnitTests.Import;

/// <summary>
/// T031: pure parsing tests for <see cref="WorkbookParser"/> against contracts/import-file.md —
/// header matching, row status assignment (Valid/StyleMismatch/Invalid), duplicate-pair
/// detection, Collaborators splitting, and style matching by exact code or exact name.
/// </summary>
public sealed class WorkbookParserTests
{
    private static readonly string[] StandardHeaders =
        ["ParticipantName", "ParticipantEmail", "BeerName", "Style", "Collaborators"];

    private static readonly IReadOnlyCollection<StyleCatalogEntry> Styles =
    [
        new StyleCatalogEntry("21A", "American IPA"),
        new StyleCatalogEntry("20C", "Imperial Stout"),
    ];

    private static byte[] BuildWorkbook(string[] headers, IEnumerable<string?[]> rows, bool includeHeaderRow = true)
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
                if (row[col] is not null)
                {
                    worksheet.Cell(rowIndex, col + 1).Value = row[col];
                }
            }

            rowIndex++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] BuildStandardWorkbook(
        params (string ParticipantName, string ParticipantEmail, string BeerName, string Style, string? Collaborators)[] rows) =>
        BuildWorkbook(
            StandardHeaders,
            rows.Select(row => new[] { row.ParticipantName, row.ParticipantEmail, row.BeerName, row.Style, row.Collaborators }));

    private static IReadOnlyList<ParsedImportRow> Parse(byte[] xlsxBytes)
    {
        using var stream = new MemoryStream(xlsxBytes);
        return WorkbookParser.Parse(stream, Styles);
    }

    [Fact]
    public void Parses_well_formed_rows_as_Valid_with_matched_style_code()
    {
        var xlsx = BuildStandardWorkbook(
            ("Ana Gomez", "ana@brew.example", "Hop Cannon", "21A", "luis@brew.example"),
            ("Luis Perez", "luis@brew.example", "Dark Matter", "American IPA", null));

        var rows = Parse(xlsx);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(ImportRowStatus.Valid, row.Status));
        Assert.Equal("21A", rows[0].ResolvedStyleCode);
        Assert.Equal("21A", rows[1].ResolvedStyleCode); // matched by exact name
        Assert.Equal(1, rows[0].RowNumber);
        Assert.Equal(2, rows[1].RowNumber);
        Assert.Equal(["luis@brew.example"], rows[0].Collaborators);
        Assert.Empty(rows[1].Collaborators);
    }

    [Fact]
    public void Headers_are_matched_case_insensitively_trimmed_and_in_any_order()
    {
        var xlsx = BuildWorkbook(
            headers: [" style ", "BEERNAME", "participantemail", "ParticipantName"],
            rows: [["21A", "Hop Cannon", "ana@brew.example", "Ana Gomez"]]);

        var rows = Parse(xlsx);

        Assert.Single(rows);
        Assert.Equal(ImportRowStatus.Valid, rows[0].Status);
        Assert.Equal("Ana Gomez", rows[0].ParticipantName);
        Assert.Equal("Hop Cannon", rows[0].BeerName);
    }

    [Fact]
    public void Style_matching_by_code_is_case_insensitive()
    {
        var xlsx = BuildStandardWorkbook(("Ana Gomez", "ana@brew.example", "Hop Cannon", "21a", null));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.Valid, rows[0].Status);
        Assert.Equal("21A", rows[0].ResolvedStyleCode);
    }

    [Fact]
    public void Style_matching_by_exact_name_is_case_insensitive()
    {
        var xlsx = BuildStandardWorkbook(("Ana Gomez", "ana@brew.example", "Hop Cannon", "american ipa", null));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.Valid, rows[0].Status);
        Assert.Equal("21A", rows[0].ResolvedStyleCode);
    }

    [Fact]
    public void Unmatched_style_is_marked_StyleMismatch_without_aborting_the_load()
    {
        var xlsx = BuildStandardWorkbook(("Sam Roe", "sam@brew.example", "Fizzy Lifting", "99Z", null));

        var rows = Parse(xlsx);

        Assert.Single(rows);
        Assert.Equal(ImportRowStatus.StyleMismatch, rows[0].Status);
        Assert.Null(rows[0].ResolvedStyleCode);
        Assert.False(string.IsNullOrWhiteSpace(rows[0].ErrorMessage));
    }

    [Theory]
    [InlineData("", "ana@brew.example", "Hop Cannon", "21A")]
    [InlineData("Ana Gomez", "", "Hop Cannon", "21A")]
    [InlineData("Ana Gomez", "not-an-email", "Hop Cannon", "21A")]
    [InlineData("Ana Gomez", "ana@brew.example", "", "21A")]
    public void Rows_with_missing_or_malformed_required_cells_are_marked_Invalid(
        string participantName, string participantEmail, string beerName, string style)
    {
        var xlsx = BuildStandardWorkbook((participantName, participantEmail, beerName, style, null));

        var rows = Parse(xlsx);

        Assert.Single(rows);
        Assert.Equal(ImportRowStatus.Invalid, rows[0].Status);
        Assert.False(string.IsNullOrWhiteSpace(rows[0].ErrorMessage));
    }

    [Fact]
    public void Over_length_participant_name_is_marked_Invalid()
    {
        var xlsx = BuildStandardWorkbook((new string('A', 201), "ana@brew.example", "Hop Cannon", "21A", null));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.Invalid, rows[0].Status);
    }

    [Fact]
    public void Duplicate_participant_email_and_beer_name_pair_marks_the_second_occurrence_Invalid()
    {
        var xlsx = BuildStandardWorkbook(
            ("Ana Gomez", "ana@brew.example", "Hop Cannon", "21A", null),
            ("Ana Gomez", "ana@brew.example", "Hop Cannon", "21A", null));

        var rows = Parse(xlsx);

        Assert.Equal(2, rows.Count);
        Assert.Equal(ImportRowStatus.Valid, rows[0].Status);
        Assert.Equal(ImportRowStatus.Invalid, rows[1].Status);
    }

    [Fact]
    public void Duplicate_check_is_case_insensitive_and_trims_whitespace()
    {
        var xlsx = BuildStandardWorkbook(
            ("Ana Gomez", "Ana@Brew.example", "Hop Cannon", "21A", null),
            ("Ana Gomez", " ana@brew.example ", " Hop Cannon ", "21A", null));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.Valid, rows[0].Status);
        Assert.Equal(ImportRowStatus.Invalid, rows[1].Status);
    }

    [Fact]
    public void Collaborators_are_split_on_semicolons_and_trimmed()
    {
        var xlsx = BuildStandardWorkbook(
            ("Ana Gomez", "ana@brew.example", "Hop Cannon", "21A", " luis@brew.example ; sam@brew.example;"));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.Valid, rows[0].Status);
        Assert.Equal(["luis@brew.example", "sam@brew.example"], rows[0].Collaborators);
    }

    [Fact]
    public void Invalid_collaborator_email_marks_the_row_Invalid()
    {
        var xlsx = BuildStandardWorkbook(("Ana Gomez", "ana@brew.example", "Hop Cannon", "21A", "not-an-email"));

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.Invalid, rows[0].Status);
    }

    [Fact]
    public void Missing_Collaborators_column_entirely_is_allowed_since_it_is_optional()
    {
        var xlsx = BuildWorkbook(
            headers: ["ParticipantName", "ParticipantEmail", "BeerName", "Style"],
            rows: [["Ana Gomez", "ana@brew.example", "Hop Cannon", "21A"]]);

        var rows = Parse(xlsx);

        Assert.Equal(ImportRowStatus.Valid, rows[0].Status);
        Assert.Empty(rows[0].Collaborators);
    }

    [Fact]
    public void Parsing_stops_at_the_first_fully_empty_row()
    {
        var xlsx = BuildWorkbook(
            StandardHeaders,
            rows:
            [
                ["Ana Gomez", "ana@brew.example", "Hop Cannon", "21A", null],
                [null, null, null, null, null],
                ["Luis Perez", "luis@brew.example", "Dark Matter", "20C", null],
            ]);

        var rows = Parse(xlsx);

        Assert.Single(rows);
        Assert.Equal("Ana Gomez", rows[0].ParticipantName);
    }

    [Fact]
    public void Missing_required_header_column_throws_invalid_import_file()
    {
        var xlsx = BuildWorkbook(
            headers: ["ParticipantName", "ParticipantEmail", "BeerName"],
            rows: [["Ana Gomez", "ana@brew.example", "Hop Cannon"]]);

        var exception = Assert.Throws<DomainException>(() => Parse(xlsx));

        Assert.Equal(DomainErrorType.InvalidImportFile, exception.ErrorType);
    }

    [Fact]
    public void Zero_data_rows_throws_invalid_import_file()
    {
        var xlsx = BuildWorkbook(StandardHeaders, rows: []);

        var exception = Assert.Throws<DomainException>(() => Parse(xlsx));

        Assert.Equal(DomainErrorType.InvalidImportFile, exception.ErrorType);
    }

    [Fact]
    public void Unreadable_bytes_throws_invalid_import_file()
    {
        var corrupt = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        var exception = Assert.Throws<DomainException>(() =>
        {
            using var stream = new MemoryStream(corrupt);
            WorkbookParser.Parse(stream, Styles);
        });

        Assert.Equal(DomainErrorType.InvalidImportFile, exception.ErrorType);
    }
}
