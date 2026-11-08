using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Audit.Tests;

/// <summary>The host decides the audit table's schema through <c>ApplyAuditLog(configure)</c>.</summary>
public sealed class HostSchemaTests
{
    private sealed class SchemaDb(DbContextOptions<SchemaDb> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.ApplyAuditLog(configure: audit => audit.ToTable("AuditLogs", "trail"));
    }

    [Fact]
    public void HostSetSchemaIsHonoured()
    {
        using var db = new SchemaDb(new DbContextOptionsBuilder<SchemaDb>().UseNpgsql("Host=localhost").Options);

        var entity = db.Model.FindEntityType(typeof(AuditLog))!;

        Assert.Equal("trail", entity.GetSchema());
        Assert.Equal("AuditLogs", entity.GetTableName());
    }
}
