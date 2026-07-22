namespace BirraPoint.Api.Domain;

/// <summary>Rendered per-entry PDF scoresheet (T074/FR-040), upserted by BeerEntryId on regeneration.</summary>
public class GeneratedScoreSheet : Entity
{
    public Guid BeerEntryId { get; set; }

    public required byte[] PdfBytes { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }
}
