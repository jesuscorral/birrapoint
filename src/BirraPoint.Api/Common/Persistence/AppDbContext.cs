using BirraPoint.Api.Domain;
using BirraPoint.Api.Modules.Catalog.Domain;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Organization.Features.Import;
using BirraPoint.Api.Modules.Publication.Domain;
using BuildingBlocks.Audit;
using BuildingBlocks.Jobs;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Common.Persistence;

internal class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Competition> Competitions => Set<Competition>();

    public DbSet<Organizer> Organizers => Set<Organizer>();

    public DbSet<CompetitionCategory> CompetitionCategories => Set<CompetitionCategory>();

    public DbSet<CompetitionCategoryStyle> CompetitionCategoryStyles => Set<CompetitionCategoryStyle>();

    public DbSet<BjcpStyle> BjcpStyles => Set<BjcpStyle>();

    public DbSet<Participant> Participants => Set<Participant>();

    public DbSet<BeerEntry> BeerEntries => Set<BeerEntry>();

    public DbSet<EntryCollaborator> EntryCollaborators => Set<EntryCollaborator>();

    public DbSet<BeerImportBatch> BeerImportBatches => Set<BeerImportBatch>();

    public DbSet<BeerImportRow> BeerImportRows => Set<BeerImportRow>();

    public DbSet<Judge> Judges => Set<Judge>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<JudgeImportBatch> JudgeImportBatches => Set<JudgeImportBatch>();

    public DbSet<JudgeImportRow> JudgeImportRows => Set<JudgeImportRow>();

    public DbSet<TastingTable> TastingTables => Set<TastingTable>();

    public DbSet<TableJudge> TableJudges => Set<TableJudge>();

    public DbSet<TableSample> TableSamples => Set<TableSample>();

    public DbSet<Evaluation> Evaluations => Set<Evaluation>();

    public DbSet<DiscrepancyAlert> DiscrepancyAlerts => Set<DiscrepancyAlert>();

    public DbSet<Job> DispatchJobs => Set<Job>();

    public DbSet<GeneratedScoreSheet> GeneratedScoreSheets => Set<GeneratedScoreSheet>();

    public DbSet<ResultsArchive> ResultsArchives => Set<ResultsArchive>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Audit trail from the BuildingBlocks.Audit block (ADR-0035); DataJson is jsonb in PostgreSQL.
        modelBuilder.ApplyAuditLog(configure: audit => audit.Property(a => a.DataJson).HasColumnType("jsonb"));

        // Background job queue from the BuildingBlocks.Jobs block (R-06, ADR-0037), kept on the original
        // DispatchJobs table: GroupKey is the owning competition, deleted along with it.
        modelBuilder.ApplyJobs(job =>
        {
            job.ToTable("DispatchJobs");
            job.Property(j => j.GroupKey).HasColumnName("CompetitionId");
            job.Property(j => j.Type).HasMaxLength(30);
            job.HasOne<Competition>()
                .WithMany()
                .HasForeignKey(j => j.GroupKey)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

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

    /// <summary>CreatedAt/UpdatedAt are stamped centrally (data-model.md) — handlers never set them.
    /// <see cref="Job"/> belongs to the jobs block, which stamps its own writes; host edits to it (e.g. a
    /// manual retry) are stamped here so UpdatedAt keeps moving.</summary>
    private void StampTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Job>())
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
