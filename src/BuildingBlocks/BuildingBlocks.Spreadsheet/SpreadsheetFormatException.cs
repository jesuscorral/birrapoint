namespace BuildingBlocks.Spreadsheet;

/// <summary>The workbook cannot be read as a table: unreadable file, no worksheet or missing required columns.</summary>
public sealed class SpreadsheetFormatException : Exception
{
    public SpreadsheetFormatException()
    {
    }

    public SpreadsheetFormatException(string message)
        : base(message)
    {
    }

    public SpreadsheetFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
