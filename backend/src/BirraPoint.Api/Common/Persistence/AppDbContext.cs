using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Common.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Competition> Competitions => Set<Competition>();

    public DbSet<BjcpStyle> BjcpStyles => Set<BjcpStyle>();

    public DbSet<Participant> Participants => Set<Participant>();

    public DbSet<BeerEntry> BeerEntries => Set<BeerEntry>();

    public DbSet<EntryCollaborator> EntryCollaborators => Set<EntryCollaborator>();

    public DbSet<Judge> Judges => Set<Judge>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<TastingTable> TastingTables => Set<TastingTable>();

    public DbSet<TableJudge> TableJudges => Set<TableJudge>();

    public DbSet<TableSample> TableSamples => Set<TableSample>();

    public DbSet<Evaluation> Evaluations => Set<Evaluation>();

    public DbSet<DiscrepancyAlert> DiscrepancyAlerts => Set<DiscrepancyAlert>();

    public DbSet<DispatchJob> DispatchJobs => Set<DispatchJob>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>CreatedAt/UpdatedAt are stamped centrally (data-model.md) — handlers never set them.</summary>
    private void StampTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<ITimestamped>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
