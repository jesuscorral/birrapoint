namespace BuildingBlocks.Jobs;

/// <summary>
/// A database-persisted background job. Handlers must be idempotent: the worker may run the same job more
/// than once after a crash mid-processing. The queue targets PostgreSQL only (claims use
/// <c>FOR UPDATE SKIP LOCKED</c>).
/// </summary>
public sealed class Job
{
    public const int StatusMaxLength = 20;
    public const int LastErrorMaxLength = 2000;
    public const int LeaseOwnerMaxLength = 200;
    public const int DefaultTypeMaxLength = 100;

    /// <summary>Sequential (v7) Guid, so inserts stay index-friendly.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Host-defined grouping key (e.g. the aggregate the job belongs to); handed to
    /// <see cref="IJobObserver"/> so the host can route notifications.</summary>
    public Guid GroupKey { get; set; }

    /// <summary>Host-defined discriminator that selects the <see cref="IJobHandler"/>.</summary>
    public required string Type { get; set; }

    /// <summary>JSON payload for the handler, stored as <c>jsonb</c> on PostgreSQL.</summary>
    public required string PayloadJson { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Pending;

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    /// <summary>Null until a failed attempt schedules a backoff-delayed retry; a Pending job is eligible
    /// for dispatch only once this has passed (or is null).</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>Unique id of the worker process currently holding this job; set on claim, cleared on
    /// completion/failure. Null for any job not currently Running.</summary>
    public string? LeaseOwner { get; set; }

    /// <summary>The claim is valid until this instant; the owner renews it while the handler runs. A Running
    /// job whose lease has expired (or is null) is recoverable by any worker.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A job that failed all its attempts stays <see cref="Failed"/> until the host resets it to
/// <see cref="Pending"/>.</summary>
public enum JobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
}
