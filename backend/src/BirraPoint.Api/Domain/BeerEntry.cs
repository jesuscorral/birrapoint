namespace BirraPoint.Api.Domain;

/// <summary>Sample under evaluation. Created at import consolidation with its blind code (FR-013).</summary>
public class BeerEntry : Entity
{
    public Guid CompetitionId { get; set; }

    public Guid ParticipantId { get; set; }

    /// <summary>Never serialized into judge-facing DTOs (BR-01/FR-019).</summary>
    public required string BeerName { get; set; }

    public required string StyleCode { get; set; }

    /// <summary>System-generated at consolidation; unique per competition.</summary>
    public required string BlindCode { get; set; }

    /// <summary>Set per FR-018 when the owning/collaborating judge sits at any table.</summary>
    public bool NotValidForBos { get; set; }

    public ICollection<EntryCollaborator> Collaborators { get; set; } = [];
}
