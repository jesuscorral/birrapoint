namespace BirraPoint.Api.Domain;

/// <summary>
/// Immutable audit trail (FR-035, FR-039). DataJson holds before/after diffs only —
/// never full comment bodies beyond diffs, never credentials (data-model.md).
/// </summary>
public class AuditLog : Entity
{
    /// <summary>Keycloak subject.</summary>
    public required string ActorUserId { get; set; }

    /// <summary>E.g. EvaluationCorrected, JudgeRemoved, TableClosed, StateChanged.</summary>
    public required string Action { get; set; }

    public required string EntityType { get; set; }

    public required string EntityId { get; set; }

    /// <summary>jsonb before/after payload.</summary>
    public required string DataJson { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
