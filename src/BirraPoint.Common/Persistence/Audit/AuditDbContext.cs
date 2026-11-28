using BuildingBlocks.Audit;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Common.Persistence.Audit;

/// <summary>The <c>audit</c> schema: the immutable audit trail of the BuildingBlocks.Audit block (ADR-0035). Every module
/// writes to it through <see cref="IAuditWriter"/>; the entry is saved by the unit of work together with the module's
/// own change (FR-096).</summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options, SharedTransaction? sharedTransaction = null)
    : BirraPointDbContext(options, DbSchemas.Audit, sharedTransaction)
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // DataJson is jsonb in PostgreSQL.
        modelBuilder.ApplyAuditLog(configure: audit =>
        {
            audit.ToTable("AuditLogs", DbSchemas.Audit);
            audit.Property(a => a.DataJson).HasColumnType("jsonb");
        });
    }
}
