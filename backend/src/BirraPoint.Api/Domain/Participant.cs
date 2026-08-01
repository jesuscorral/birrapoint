namespace BirraPoint.Api.Domain;

/// <summary>Brewer — never exposed to judges (BR-01/FR-019).</summary>
public class Participant : Entity
{
    public Guid CompetitionId { get; set; }

    public required string Name { get; set; }

    /// <summary>COI matching key (FR-017); unique per competition.</summary>
    public required string Email { get; set; }

    /// <summary>ACCE club membership number (import-file.md); stored as text to preserve the
    /// organizer's own formatting rather than round-tripping through a numeric type.</summary>
    public string? AcceMemberNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? Phone { get; set; }
}
