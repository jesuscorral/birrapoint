using BirraPoint.Api.Domain;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Organization.Features.Import;
using BirraPoint.Api.Modules.Publication.Domain;
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

    public DbSet<DispatchJob> DispatchJobs => Set<DispatchJob>();

    public DbSet<GeneratedScoreSheet> GeneratedScoreSheets => Set<GeneratedScoreSheet>();

    public DbSet<ResultsArchive> ResultsArchives => Set<ResultsArchive>();

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
