using ClosedXML.Excel;

namespace BuildingBlocks.Spreadsheet;

/// <summary>
/// Reads the first worksheet of an .xlsx workbook as a table: row 1 is the header (case-insensitive,
/// trimmed, order-independent, first occurrence of a duplicate wins) and data rows run from row 2 up to
/// the first fully empty row.
/// </summary>
public sealed class SpreadsheetReader : IDisposable
{
    private readonly XLWorkbook _workbook;
    private readonly IXLWorksheet _worksheet;
    private readonly Dictionary<string, int> _columns;

    private SpreadsheetReader(XLWorkbook workbook, IXLWorksheet worksheet, Dictionary<string, int> columns)
    {
        _workbook = workbook;
        _worksheet = worksheet;
        _columns = columns;
    }

    /// <summary>Opens the first worksheet of the workbook in <paramref name="stream"/>.</summary>
    /// <exception cref="SpreadsheetFormatException">Not a readable .xlsx workbook, or it has no worksheet.</exception>
    public static SpreadsheetReader Open(Stream stream)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new SpreadsheetFormatException("The uploaded file is not a readable .xlsx workbook.", ex);
        }

        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet is null)
        {
            workbook.Dispose();
            throw new SpreadsheetFormatException("The workbook has no worksheets.");
        }

        return new SpreadsheetReader(workbook, worksheet, ReadHeaderColumns(worksheet.Row(1)));
    }

    /// <summary>Reports every missing header at once.</summary>
    /// <exception cref="SpreadsheetFormatException">At least one header is missing.</exception>
    public void RequireHeaders(params string[] headers)
    {
        var missing = headers.Where(header => !_columns.ContainsKey(header)).ToList();
        if (missing.Count > 0)
        {
            throw new SpreadsheetFormatException($"Missing required column(s): {string.Join(", ", missing)}.");
        }
    }

    /// <summary>Data rows from row 2, stopping at the first fully empty row. Enumerate before disposing the reader.</summary>
    public IEnumerable<SpreadsheetRow> ReadRows()
    {
        var lastRowUsed = _worksheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var excelRow = 2; excelRow <= lastRowUsed; excelRow++)
        {
            var row = _worksheet.Row(excelRow);
            if (row.IsEmpty())
            {
                yield break;
            }

            yield return new SpreadsheetRow(row, _columns);
        }
    }

    public void Dispose() => _workbook.Dispose();

    private static Dictionary<string, int> ReadHeaderColumns(IXLRow headerRow)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lastHeaderColumn = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;

        for (var col = 1; col <= lastHeaderColumn; col++)
        {
            var header = headerRow.Cell(col).GetString().Trim();
            if (header.Length > 0 && !columns.ContainsKey(header))
            {
                columns[header] = col;
            }
        }

        return columns;
    }
}
