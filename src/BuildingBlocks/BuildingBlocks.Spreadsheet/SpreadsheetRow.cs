using System.Globalization;
using ClosedXML.Excel;

namespace BuildingBlocks.Spreadsheet;

/// <summary>One data row, addressed by header name. A header absent from the sheet reads as an absent cell.</summary>
public sealed class SpreadsheetRow
{
    private readonly IXLRow _row;
    private readonly IReadOnlyDictionary<string, int> _columns;

    internal SpreadsheetRow(IXLRow row, IReadOnlyDictionary<string, int> columns)
    {
        _row = row;
        _columns = columns;
    }

    public string? ReadText(string header)
    {
        if (!TryColumn(header, out var column))
        {
            return null;
        }

        var value = _row.Cell(column).GetString().Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>Reads a cell typed as a genuine Excel number or as free text; either way a number comes back
    /// as plain digits, never a decimal/scientific-notation artifact.</summary>
    public string? ReadNumericOrText(string header)
    {
        if (!TryCell(header, out var cell))
        {
            return null;
        }

        if (cell.DataType == XLDataType.Number)
        {
            var number = cell.GetDouble();
            return double.IsInteger(number)
                ? ((long)number).ToString(CultureInfo.InvariantCulture)
                : number.ToString(CultureInfo.InvariantCulture);
        }

        var text = cell.GetString().Trim();
        return text.Length == 0 ? null : text;
    }

    public CellValue<DateOnly> ReadDate(string header)
    {
        if (!TryCell(header, out var cell))
        {
            return CellValue<DateOnly>.Absent;
        }

        if (cell.DataType == XLDataType.DateTime)
        {
            return CellValue<DateOnly>.Of(DateOnly.FromDateTime(cell.GetDateTime()));
        }

        var text = cell.GetString().Trim();
        if (text.Length == 0)
        {
            return CellValue<DateOnly>.Absent;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? CellValue<DateOnly>.Of(DateOnly.FromDateTime(parsed))
            : CellValue<DateOnly>.Invalid;
    }

    public CellValue<DateTime> ReadDateTime(string header)
    {
        if (!TryCell(header, out var cell))
        {
            return CellValue<DateTime>.Absent;
        }

        if (cell.DataType == XLDataType.DateTime)
        {
            return CellValue<DateTime>.Of(cell.GetDateTime());
        }

        var text = cell.GetString().Trim();
        if (text.Length == 0)
        {
            return CellValue<DateTime>.Absent;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? CellValue<DateTime>.Of(parsed)
            : CellValue<DateTime>.Invalid;
    }

    public CellValue<decimal> ReadNumber(string header)
    {
        if (!TryCell(header, out var cell))
        {
            return CellValue<decimal>.Absent;
        }

        if (cell.DataType == XLDataType.Number)
        {
            return CellValue<decimal>.Of((decimal)cell.GetDouble());
        }

        var text = cell.GetString().Trim();
        if (text.Length == 0)
        {
            return CellValue<decimal>.Absent;
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? CellValue<decimal>.Of(parsed)
            : CellValue<decimal>.Invalid;
    }

    private bool TryColumn(string header, out int column) =>
        _columns.TryGetValue(header, out column) && column > 0;

    /// <summary>The cell when the column exists and the cell is not empty.</summary>
    private bool TryCell(string header, out IXLCell cell)
    {
        cell = null!;
        if (!TryColumn(header, out var column))
        {
            return false;
        }

        cell = _row.Cell(column);
        return !cell.IsEmpty();
    }
}
