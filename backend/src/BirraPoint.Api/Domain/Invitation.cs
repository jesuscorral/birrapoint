namespace BirraPoint.Api.Domain;

/// <summary>Per-recipient invitation delivery state (R-10, FR-016).</summary>
public class Invitation : Entity
{
    public Guid JudgeId { get; set; }

    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

    public int Attempts { get; set; }

    /// <summary>Last SMTP failure, if any.</summary>
    public string? LastError { get; set; }

    public DateTimeOffset? SentAt { get; set; }
}
