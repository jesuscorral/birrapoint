namespace BirraPoint.Api.Domain;

/// <summary>
/// Order fix is one-shot (second attempt → 409). Closed requires every (judge × sample)
/// evaluation submitted and zero open DiscrepancyAlert; Closed is terminal (data-model.md).
/// </summary>
public class TastingTable : Entity
{
    public Guid CompetitionId { get; set; }

    public required string Name { get; set; }

    public TableState State { get; set; } = TableState.Open;

    /// <summary>Null until the order is fixed (US6).</summary>
    public Guid? OrderFixedByJudgeId { get; set; }

    public DateTimeOffset? OrderFixedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public ICollection<TableJudge> Judges { get; set; } = [];

    public ICollection<TableSample> Samples { get; set; } = [];
}
