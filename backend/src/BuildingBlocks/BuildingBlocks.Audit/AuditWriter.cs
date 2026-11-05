using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Audit;

/// <summary><see cref="IAuditWriter"/> over the host's <typeparamref name="TContext"/>; register with
/// <see cref="AuditServiceCollectionExtensions.AddAudit{TContext, TActorAccessor}"/>.</summary>
public sealed class AuditWriter<TContext>(TContext dbContext, IAuditActorAccessor actorAccessor, TimeProvider timeProvider)
    : IAuditWriter
    where TContext : DbContext
{
    public void Record(string action, string entityType, string entityId, object? before = null, object? after = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        dbContext.Set<AuditLog>().Add(new AuditLog
        {
            ActorUserId = actorAccessor.ActorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            DataJson = JsonSerializer.Serialize(new { before, after }),
            OccurredAt = timeProvider.GetUtcNow(),
        });
    }
}
