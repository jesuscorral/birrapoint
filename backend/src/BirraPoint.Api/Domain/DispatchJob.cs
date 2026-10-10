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

    /// <summary>Null until a failed attempt schedules a backoff-delayed retry (ADR-0008); a
    /// Pending job is eligible for dispatch only once this has passed (or is null).</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>Unique id of the worker process currently holding this job (T129); set on claim,
    /// cleared on completion/failure. Null for any job not currently Running.</summary>
    public string? LeaseOwner { get; set; }

    /// <summary>The claim is valid until this instant; the owner renews it while the handler
    /// runs. A Running job whose lease has expired (or is null) is recoverable by any worker.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }
}
