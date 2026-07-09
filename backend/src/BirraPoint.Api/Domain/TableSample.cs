namespace BirraPoint.Api.Domain;

/// <summary>Composite PK (TastingTableId, BeerEntryId); an entry belongs to at most one table.</summary>
public class TableSample : ITimestamped
{
    public Guid TastingTableId { get; set; }

    public Guid BeerEntryId { get; set; }

    /// <summary>Null until the order is fixed, then 1..M unique within the table (US6).</summary>
    public int? SequenceOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
