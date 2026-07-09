namespace BirraPoint.Api.Common.Audit;

/// <summary>Stages an immutable audit trail row (FR-035, FR-039) for the current actor.</summary>
public interface IAuditWriter
{
    /// <summary>
    /// Stages an <see cref="Domain.AuditLog"/> row via the DbContext's change tracker — does
    /// **not** call SaveChanges itself, so the audit entry commits atomically together with
    /// whatever business change the caller's own handler persists, in the same transaction.
    /// </summary>
    void Record(string action, string entityType, string entityId, object? before = null, object? after = null);
}
