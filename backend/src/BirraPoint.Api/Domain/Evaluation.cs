namespace BirraPoint.Api.Domain;

/// <summary>
/// Immutable once submitted except via an open DiscrepancyAlert involving this judge
/// (FR-030/FR-031/FR-034). Score caps and comment lengths are enforced at the API boundary;
/// the (JudgeId, BeerEntryId) unique index is the idempotency backstop (FR-029, R-07).
/// </summary>
public class Evaluation : Entity
{
    public Guid TastingTableId { get; set; }

    public Guid JudgeId { get; set; }

    public Guid BeerEntryId { get; set; }

    /// <summary>0–12 (FR-024).</summary>
    public int AromaScore { get; set; }

    /// <summary>0–3 (FR-024).</summary>
    public int AppearanceScore { get; set; }

    /// <summary>0–20 (FR-024).</summary>
    public int FlavorScore { get; set; }

    /// <summary>0–5 (FR-024).</summary>
    public int MouthfeelScore { get; set; }

    /// <summary>0–10 (FR-024).</summary>
    public int OverallScore { get; set; }

    public required string AromaComment { get; set; }

    public required string AppearanceComment { get; set; }

    public required string FlavorComment { get; set; }

    public required string MouthfeelComment { get; set; }

    public required string OverallComment { get; set; }

    /// <summary>DB-computed (stored generated column) = sum of the five scores; never client-supplied (FR-024).</summary>
    public int Total { get; private set; }

    public EvaluationStatus Status { get; set; } = EvaluationStatus.Confirmed;

    public DateTimeOffset SubmittedAt { get; set; }
}
