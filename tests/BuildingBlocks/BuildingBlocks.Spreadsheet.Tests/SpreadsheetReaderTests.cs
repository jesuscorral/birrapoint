using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;

namespace BuildingBlocks.Spreadsheet.Tests;

public sealed class SpreadsheetReaderTests
{
    private static MemoryStream Workbook(Action<IXLWorksheet> fill)
    {
        using var workbook = new XLWorkbook();
        fill(workbook.AddWorksheet("Sheet"));
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream Table(string[] headers, params object[][] rows) =>
        Workbook(sheet =>
        {
            for (var c = 0; c < headers.Length; c++)
            {
                sheet.Cell(1, c + 1).Value = headers[c];
            }

            for (var r = 0; r < rows.Length; r++)
            {
                for (var c = 0; c < rows[r].Length; c++)
                {
                    sheet.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(rows[r][c], CultureInfo.InvariantCulture);
                }
            }
        });

    private static T Read<T>(object value, Func<SpreadsheetRow, T> read)
    {
        using var reader = SpreadsheetReader.Open(Table(["A", "Other"], [value, "x"]));
        return read(reader.ReadRows().Single());
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }

    [Fact]
    public void OpenUnreadableBytesThrowsFormatException()
    {
        var ex = Assert.Throws<SpreadsheetFormatException>(() => SpreadsheetReader.Open(new MemoryStream([1, 2, 3, 4])));

        Assert.Equal("The uploaded file is not a readable .xlsx workbook.", ex.Message);
    }

    [Fact]
    public void OpenWorkbookWithoutWorksheetsThrowsFormatException()
    {
        // ClosedXML refuses to save a sheetless workbook, so the package is built by hand.
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/></Types>""");
            Write(zip, "_rels/.rels", """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Write(zip, "xl/workbook.xml", """<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheets/></workbook>""");
        }

        stream.Position = 0;

        var ex = Assert.Throws<SpreadsheetFormatException>(() => SpreadsheetReader.Open(stream));

        Assert.Equal("The workbook has no worksheets.", ex.Message);
    }

    [Fact]
    public void HeadersAreCaseInsensitiveTrimmedAndOrderIndependent()
    {
        using var reader = SpreadsheetReader.Open(Table(["  NAME ", "email"], ["Ana", "a@b.co"]));

        reader.RequireHeaders("email", "Name");
        var row = reader.ReadRows().Single();

        Assert.Equal("Ana", row.ReadText("name"));
        Assert.Equal("a@b.co", row.ReadText("EMAIL"));
    }

    [Fact]
    public void HeadersDuplicateFirstOccurrenceWins()
    {
        using var reader = SpreadsheetReader.Open(Table(["Name", "name"], ["first", "second"]));

        Assert.Equal("first", reader.ReadRows().Single().ReadText("Name"));
    }

    [Fact]
    public void RequireHeadersListsAllMissingAtOnce()
    {
        using var reader = SpreadsheetReader.Open(Table(["A"], ["x"]));

        var ex = Assert.Throws<SpreadsheetFormatException>(() => reader.RequireHeaders("A", "B", "C"));

        Assert.Equal("Missing required column(s): B, C.", ex.Message);
    }

    [Fact]
    public void ReadRowsStopsAtFirstEmptyRow()
    {
        using var reader = SpreadsheetReader.Open(Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = "A";
            sheet.Cell(2, 1).Value = "one";
            sheet.Cell(4, 1).Value = "after gap";
        }));

        var rows = reader.ReadRows().ToList();

        Assert.Single(rows);
        Assert.Equal("one", rows[0].ReadText("A"));
    }

    [Fact]
    public void ReadRowsHeaderOnlyYieldsNothing()
    {
        using var reader = SpreadsheetReader.Open(Table(["A"]));

        Assert.Empty(reader.ReadRows());
    }

    [Fact]
    public void AbsentHeaderReadsAsAbsentCell()
    {
        Assert.Null(Read("x", r => r.ReadText("Missing")));
        Assert.Null(Read("x", r => r.ReadNumericOrText("Missing")));
        Assert.Equal(new CellValue<DateOnly>(null, false, false), Read("x", r => r.ReadDate("Missing")));
        Assert.Equal(new CellValue<DateTime>(null, false, false), Read("x", r => r.ReadDateTime("Missing")));
        Assert.Equal(new CellValue<decimal>(null, false, false), Read("x", r => r.ReadNumber("Missing")));
    }

    [Fact]
    public void ReadTextTrimsAndTreatsBlankAsNull()
    {
        Assert.Equal("hi", Read("  hi  ", r => r.ReadText("A")));
        Assert.Null(Read("   ", r => r.ReadText("A")));
        Assert.Null(Read(string.Empty, r => r.ReadText("A")));
    }

    [Fact]
    public void ReadNumericOrTextNumbersBecomePlainDigits()
    {
        Assert.Equal("600123456", Read(600123456d, r => r.ReadNumericOrText("A")));
        Assert.Equal("120000", Read(120000d, r => r.ReadNumericOrText("A")));
        Assert.Equal("1.5", Read(1.5d, r => r.ReadNumericOrText("A")));
        Assert.Equal("+34 600", Read(" +34 600 ", r => r.ReadNumericOrText("A")));
        Assert.Null(Read(string.Empty, r => r.ReadNumericOrText("A")));
    }

    [Fact]
    public void ReadDateCoversEmptyDateTextAndInvalidText()
    {
        Assert.Equal(new CellValue<DateOnly>(null, false, false), Read(string.Empty, r => r.ReadDate("A")));
        Assert.Equal(new CellValue<DateOnly>(new DateOnly(2026, 3, 4), true, false), Read(new DateTime(2026, 3, 4, 10, 0, 0, DateTimeKind.Unspecified), r => r.ReadDate("A")));
        Assert.Equal(new CellValue<DateOnly>(new DateOnly(2026, 3, 4), true, false), Read("2026-03-04", r => r.ReadDate("A")));
        Assert.Equal(new CellValue<DateOnly>(null, true, true), Read("not a date", r => r.ReadDate("A")));
    }

    [Fact]
    public void ReadDateTimeCoversEmptyDateTextAndInvalidText()
    {
        var moment = new DateTime(2026, 3, 4, 10, 30, 0, DateTimeKind.Unspecified);

        Assert.Equal(new CellValue<DateTime>(null, false, false), Read(string.Empty, r => r.ReadDateTime("A")));
        Assert.Equal(new CellValue<DateTime>(moment, true, false), Read(moment, r => r.ReadDateTime("A")));
        Assert.Equal(new CellValue<DateTime>(moment, true, false), Read("2026-03-04 10:30:00", r => r.ReadDateTime("A")));
        Assert.Equal(new CellValue<DateTime>(null, true, true), Read("nope", r => r.ReadDateTime("A")));
    }

    [Fact]
    public void ReadNumberCoversEmptyNumberTextAndInvalidText()
    {
        Assert.Equal(new CellValue<decimal>(null, false, false), Read(string.Empty, r => r.ReadNumber("A")));
        Assert.Equal(new CellValue<decimal>(5.5m, true, false), Read(5.5d, r => r.ReadNumber("A")));
        Assert.Equal(new CellValue<decimal>(4.8m, true, false), Read("4.8", r => r.ReadNumber("A")));
        Assert.Equal(new CellValue<decimal>(null, true, true), Read("abc", r => r.ReadNumber("A")));
    }
}
