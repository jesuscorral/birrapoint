namespace BirraPoint.Api.Domain;

/// <summary>
/// At most one open alert per (TastingTableId, BeerEntryId) — partial unique index.
/// Involved judges are derived at read time (> 7 points apart, FR-031).
/// </summary>
public class DiscrepancyAlert : Entity
{
    public Guid TastingTableId { get; set; }

    public Guid BeerEntryId { get; set; }

    public DiscrepancyStatus Status { get; set; } = DiscrepancyStatus.Open;

    public DateTimeOffset? ResolvedAt { get; set; }
}
