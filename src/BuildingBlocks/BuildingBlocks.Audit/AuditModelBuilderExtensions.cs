using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildingBlocks.Audit;

public static class AuditModelBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="AuditLog"/> into the host model with provider-neutral defaults (key, required columns,
    /// lengths). <paramref name="configure"/> refines it for the host's provider, e.g. a JSON column type for
    /// <see cref="AuditLog.DataJson"/>.
    /// </summary>
    public static ModelBuilder ApplyAuditLog(
        this ModelBuilder modelBuilder, string tableName = "AuditLogs", Action<EntityTypeBuilder<AuditLog>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        modelBuilder.Entity<AuditLog>(audit =>
        {
            audit.ToTable(tableName);
            audit.HasKey(a => a.Id);
            audit.Property(a => a.ActorUserId).HasMaxLength(AuditLog.ActorUserIdMaxLength);
            audit.Property(a => a.Action).HasMaxLength(AuditLog.ActionMaxLength);
            audit.Property(a => a.EntityType).HasMaxLength(AuditLog.EntityTypeMaxLength);
            audit.Property(a => a.EntityId).HasMaxLength(AuditLog.EntityIdMaxLength);
            audit.Property(a => a.DataJson).IsRequired();
            configure?.Invoke(audit);
        });

        return modelBuilder;
    }
}
