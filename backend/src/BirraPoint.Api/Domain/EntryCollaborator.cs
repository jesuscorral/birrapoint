namespace BirraPoint.Api.Domain;

/// <summary>Composite PK (BeerEntryId, Email); email is a COI matching key (FR-017).</summary>
public class EntryCollaborator : ITimestamped
{
    public Guid BeerEntryId { get; set; }

    public required string Email { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
