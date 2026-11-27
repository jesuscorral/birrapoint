using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Domain;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Persistence;

/// <summary>The <c>judging</c> schema: tasting tables, samples, evaluations and discrepancy alerts, plus the module's
/// outbox and inbox (FR-076-FR-080). <c>CompetitionId</c>, <c>JudgeId</c> and <c>BeerEntryId</c> are plain ids of
/// Organization's data, validated through its contracts, never foreign keys (FR-093).</summary>
internal sealed class JudgingDbContext(DbContextOptions<JudgingDbContext> options, SharedTransaction? sharedTransaction = null)
    : BirraPointDbContext(options, DbSchemas.Judging, sharedTransaction)
{
    public DbSet<TastingTable> TastingTables => Set<TastingTable>();

    public DbSet<TableJudge> TableJudges => Set<TableJudge>();

    public DbSet<TableSample> TableSamples => Set<TableSample>();

    public DbSet<Evaluation> Evaluations => Set<Evaluation>();

    public DbSet<DiscrepancyAlert> DiscrepancyAlerts => Set<DiscrepancyAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyConfigurationsOf(modelBuilder, "BirraPoint.Api.Modules.Judging.Persistence");

        modelBuilder.ApplyOutbox(JudgingModule.Messaging, DbSchemas.Judging);
        modelBuilder.ApplyInbox(JudgingModule.Messaging, DbSchemas.Judging);
    }
}
