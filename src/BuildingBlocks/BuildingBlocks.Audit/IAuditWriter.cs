namespace BuildingBlocks.Audit;

/// <summary>Records immutable audit trail entries for the current actor.</summary>
public interface IAuditWriter
{
    /// <summary>
    /// Stages an <see cref="AuditLog"/> row on the host DbContext's change tracker. It does not call
    /// SaveChanges: the entry commits atomically with whatever business change the caller persists, in the
    /// same transaction, and is discarded if the caller never saves.
    /// </summary>
    /// <param name="action">What happened, e.g. <c>OrderCorrected</c>.</param>
    /// <param name="entityType">Kind of the affected entity.</param>
    /// <param name="entityId">Identifier of the affected entity.</param>
    /// <param name="before">State before the change (null for creations); serialized to JSON.</param>
    /// <param name="after">State after the change (null for deletions); serialized to JSON.</param>
    void Record(string action, string entityType, string entityId, object? before = null, object? after = null);
}
