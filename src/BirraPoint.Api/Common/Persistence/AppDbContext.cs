using BirraPoint.Api.Domain;
using BirraPoint.Api.Modules.Catalog.Domain;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Organization.Features.Import;
using BirraPoint.Api.Modules.Publication.Domain;
using BuildingBlocks.Audit;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging;
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
        MapModuleEntitiesToSchemas(modelBuilder);

        // Audit trail from the BuildingBlocks.Audit block (ADR-0035); DataJson is jsonb in PostgreSQL.
        modelBuilder.ApplyAuditLog(configure: audit =>
        {
            audit.ToTable("AuditLogs", DbSchemas.Audit);
            audit.Property(a => a.DataJson).HasColumnType("jsonb");
        });

        // Background job queue from the BuildingBlocks.Jobs block (R-06, ADR-0037), kept on the original
        // DispatchJobs table: GroupKey is the owning competition, deleted along with it.
        modelBuilder.ApplyJobs(job =>
        {
            job.ToTable("DispatchJobs", DbSchemas.Jobs);
            job.Property(j => j.GroupKey).HasColumnName("CompetitionId");
            job.Property(j => j.Type).HasMaxLength(30);
            job.HasOne<Competition>()
                .WithMany()
                .HasForeignKey(j => j.GroupKey)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Integration events from the BuildingBlocks.Messaging block (FR-076-FR-080): the module that publishes
        // owns an outbox in its schema, the module that subscribes owns an inbox in its schema.
        modelBuilder.ApplyOutbox("Organization", DbSchemas.Organization);
        modelBuilder.ApplyInbox("Organization", DbSchemas.Organization);
        modelBuilder.ApplyOutbox("Judging", DbSchemas.Judging);
        modelBuilder.ApplyInbox("Judging", DbSchemas.Judging);
        modelBuilder.ApplyInbox("Publication", DbSchemas.Publication);
    }

    /// <summary>The migration history sits in its own schema, outside every module (FR-073). Added here so every
    /// construction site (host, design-time factory, test fixtures) gets it.</summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", DbSchemas.Migrations));

    /// <summary>Each module entity goes to its owner's schema by namespace (FR-068); a new entity cannot be
    /// forgotten, and one outside the known modules fails the model build.</summary>
    private static void MapModuleEntitiesToSchemas(ModelBuilder modelBuilder)
    {
        const string prefix = "BirraPoint.Api.Modules.";
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(e => !e.IsOwned()))
        {
            var ns = entity.ClrType.Namespace;
            if (ns is null || !ns.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var module = ns[prefix.Length..].Split('.')[0];
            if (!DbSchemas.ByModule.TryGetValue(module, out var schema))
            {
                throw new InvalidOperationException($"Entity {entity.ClrType} is in unknown module '{module}'; add its schema to {nameof(DbSchemas)}.");
            }

            entity.SetSchema(schema);
        }
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
