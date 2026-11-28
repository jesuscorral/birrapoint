using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Organization.Features.Import;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Persistence;

/// <summary>The <c>organization</c> schema: competitions, categories, participants, entries, judges, invitations and
/// the two import pipelines, plus the module's outbox and inbox (FR-076-FR-080). References to other modules
/// (<c>StyleCode</c>) are plain values validated through their contracts, never foreign keys (FR-093).</summary>
internal sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options, SharedTransaction? sharedTransaction = null)
    : BirraPointDbContext(options, DbSchemas.Organization, sharedTransaction)
{
    public DbSet<Competition> Competitions => Set<Competition>();

    public DbSet<Organizer> Organizers => Set<Organizer>();

    public DbSet<CompetitionCategory> CompetitionCategories => Set<CompetitionCategory>();

    public DbSet<CompetitionCategoryStyle> CompetitionCategoryStyles => Set<CompetitionCategoryStyle>();

    public DbSet<Participant> Participants => Set<Participant>();

    public DbSet<BeerEntry> BeerEntries => Set<BeerEntry>();

    public DbSet<EntryCollaborator> EntryCollaborators => Set<EntryCollaborator>();

    public DbSet<BeerImportBatch> BeerImportBatches => Set<BeerImportBatch>();

    public DbSet<BeerImportRow> BeerImportRows => Set<BeerImportRow>();

    public DbSet<Judge> Judges => Set<Judge>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<JudgeImportBatch> JudgeImportBatches => Set<JudgeImportBatch>();

    public DbSet<JudgeImportRow> JudgeImportRows => Set<JudgeImportRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyConfigurationsOf(modelBuilder, "BirraPoint.Api.Modules.Organization.Persistence");

        modelBuilder.ApplyOutbox(OrganizationModule.Messaging, DbSchemas.Organization);
        modelBuilder.ApplyInbox(OrganizationModule.Messaging, DbSchemas.Organization);
    }
}
