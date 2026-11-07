namespace BuildingBlocks.Spreadsheet;

/// <summary>A typed cell read: <see cref="IsPresent"/> is false for an absent column or empty cell,
/// <see cref="IsError"/> is true when text was present but did not parse.</summary>
public readonly record struct CellValue<T>(T? Value, bool IsPresent, bool IsError)
    where T : struct
{
    internal static CellValue<T> Absent => new(null, false, false);

    internal static CellValue<T> Of(T value) => new(value, true, false);

    internal static CellValue<T> Invalid => new(null, true, true);
}
