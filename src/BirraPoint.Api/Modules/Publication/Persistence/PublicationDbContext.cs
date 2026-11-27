using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Publication.Domain;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Publication.Persistence;

/// <summary>The <c>publication</c> schema: generated score sheets and results archives, plus the module's inbox
/// (FR-076-FR-080). <c>CompetitionId</c> and <c>BeerEntryId</c> are plain ids of Organization's data, never foreign
/// keys (FR-093). The dispatch jobs live in the Jobs context.</summary>
internal sealed class PublicationDbContext(DbContextOptions<PublicationDbContext> options, SharedTransaction? sharedTransaction = null)
    : BirraPointDbContext(options, DbSchemas.Publication, sharedTransaction)
{
    public DbSet<GeneratedScoreSheet> GeneratedScoreSheets => Set<GeneratedScoreSheet>();

    public DbSet<ResultsArchive> ResultsArchives => Set<ResultsArchive>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyConfigurationsOf(modelBuilder, "BirraPoint.Api.Modules.Publication.Persistence");

        modelBuilder.ApplyInbox(PublicationModule.Messaging, DbSchemas.Publication);
    }
}
