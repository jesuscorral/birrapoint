using System.Text.Json;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Common.Audit;

public sealed class AuditWriter(AppDbContext dbContext, ICurrentUser currentUser) : IAuditWriter
{
    public void Record(string action, string entityType, string entityId, object? before = null, object? after = null)
    {
        var dataJson = JsonSerializer.Serialize(new { before, after });

        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = currentUser.Sub,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            DataJson = dataJson,
            OccurredAt = DateTimeOffset.UtcNow,
        });
    }
}
