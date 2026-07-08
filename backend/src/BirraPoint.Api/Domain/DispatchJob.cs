namespace BirraPoint.Api.Domain;

/// <summary>DB-persisted background job (R-06 — no broker); handlers must be idempotent.</summary>
public class DispatchJob : Entity
{
    public Guid CompetitionId { get; set; }

    public DispatchJobType Type { get; set; }

    /// <summary>jsonb payload, e.g. { "participantId": … }.</summary>
    public required string PayloadJson { get; set; }

    public DispatchJobStatus Status { get; set; } = DispatchJobStatus.Pending;

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}
