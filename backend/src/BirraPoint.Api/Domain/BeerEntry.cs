namespace BirraPoint.Api.Domain;

/// <summary>Sample under evaluation. Created at import consolidation with its blind code (FR-013).</summary>
public class BeerEntry : Entity
{
    public Guid CompetitionId { get; set; }

    public Guid ParticipantId { get; set; }

    /// <summary>Never serialized into judge-facing DTOs (BR-01/FR-019). Optional — the ACCE import
    /// format carries no beer-name column; always null coming out of import, the organizer may
    /// type one in later during review, or leave it null indefinitely (blind code + style +
    /// category already fully identify the sample).</summary>
    public string? BeerName { get; set; }

    public required string StyleCode { get; set; }

    /// <summary>System-generated at consolidation; unique per competition.</summary>
    public required string BlindCode { get; set; }

    /// <summary>Organizer-defined allow-list grouping (wizard step 3, CompetitionCategory) this
    /// entry was imported under. Nullable only because entries seeded outside the Import slice
    /// (this codebase's other feature test fixtures predate this field) have no category to set;
    /// the Import consolidation flow always populates it for entries created through that path.</summary>
    public Guid? CompetitionCategoryId { get; set; }

    /// <summary>"Marca temporal" — when the organizer's original entry form was submitted.</summary>
    public DateTimeOffset SubmittedAt { get; set; }

    public decimal AbvPercent { get; set; }

    public DateOnly? BrewDate { get; set; }

    public DateOnly? BottlingDate { get; set; }

    public string? Malts { get; set; }

    public string? Hops { get; set; }

    public string? Yeast { get; set; }

    public string? OtherIngredients { get; set; }

    /// <summary>Judge-facing — the one deliberate exception to BR-01/FR-019 alongside BlindCode/StyleCode.</summary>
    public string? EntryInstructions { get; set; }

    /// <summary>Set per FR-018 when the owning/collaborating judge sits at any table.</summary>
    public bool NotValidForBos { get; set; }

    public ICollection<EntryCollaborator> Collaborators { get; set; } = [];
}
