using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BuildingBlocks.Audit.Tests;

/// <summary>ApplyAuditLog maps the entity with provider-neutral defaults the host can refine. One context
/// type per configuration: EF Core caches the model per context type.</summary>
public class AuditModelTests
{
    private sealed class DefaultContext() : DbContext(
        new DbContextOptionsBuilder<DefaultContext>().UseNpgsql("Host=unused").Options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyAuditLog();
    }

    private sealed class CustomContext() : DbContext(
        new DbContextOptionsBuilder<CustomContext>().UseNpgsql("Host=unused").Options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.ApplyAuditLog("audit_trail", audit => audit.Property(a => a.EntityId).HasMaxLength(80));
    }

    private static IEntityType AuditEntity(DbContext context) => context.Model.FindEntityType(typeof(AuditLog))!;

    [Fact]
    public void MapsToAuditLogsWithDefaultLengths()
    {
        using var context = new DefaultContext();
        var entity = AuditEntity(context);

        Assert.Equal("AuditLogs", entity.GetTableName());
        Assert.Equal("Id", Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
        Assert.Equal(AuditLog.ActorUserIdMaxLength, entity.FindProperty(nameof(AuditLog.ActorUserId))!.GetMaxLength());
        Assert.Equal(AuditLog.ActionMaxLength, entity.FindProperty(nameof(AuditLog.Action))!.GetMaxLength());
        Assert.Equal(AuditLog.EntityTypeMaxLength, entity.FindProperty(nameof(AuditLog.EntityType))!.GetMaxLength());
        Assert.Equal(AuditLog.EntityIdMaxLength, entity.FindProperty(nameof(AuditLog.EntityId))!.GetMaxLength());
        Assert.Null(entity.FindProperty(nameof(AuditLog.DataJson))!.GetMaxLength());
    }

    [Fact]
    public void HonoursACustomTableNameAndHostConfiguration()
    {
        using var context = new CustomContext();
        var entity = AuditEntity(context);

        Assert.Equal("audit_trail", entity.GetTableName());
        Assert.Equal(80, entity.FindProperty(nameof(AuditLog.EntityId))!.GetMaxLength());
    }
}
