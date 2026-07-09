namespace BirraPoint.Api.Domain;

/// <summary>
/// Composite PK (TastingTableId, JudgeId). COI-checked transactionally at assignment (FR-017).
/// Rows are never hard-deleted once evaluations exist — live removal sets RemovedAt (FR-039).
/// </summary>
public class TableJudge : ITimestamped
{
    public Guid TastingTableId { get; set; }

    public Guid JudgeId { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
