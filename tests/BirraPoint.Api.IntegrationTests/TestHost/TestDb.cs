using BirraPoint.Api.Common.Persistence.Audit;
using BirraPoint.Api.Common.Persistence.Jobs;
using BirraPoint.Api.Modules.Catalog.Persistence;
using BirraPoint.Api.Modules.Judging.Persistence;
using BirraPoint.Api.Modules.Organization.Persistence;
using BirraPoint.Api.Modules.Publication.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.TestHost;

/// <summary>
/// Test-side view over the six data contexts (one per module plus jobs and audit; FR-091). Every set belongs to
/// exactly one context, so a query never spans modules; <see cref="SaveChangesAsync"/> saves the contexts one after
/// another, which is enough to seed or inspect rows (there are no cross-schema foreign keys, FR-093). Resolved from a
/// scope the contexts share one connection, so they are used sequentially, never concurrently.
/// </summary>
internal sealed class TestDb : IAsyncDisposable
{
    private readonly bool _ownsContexts;

    private TestDb(
        OrganizationDbContext organization,
        JudgingDbContext judging,
        PublicationDbContext publication,
        CatalogDbContext catalog,
        JobsDbContext jobs,
        AuditDbContext audit,
        bool ownsContexts)
    {
        Organization = organization;
        Judging = judging;
        Publication = publication;
        Catalog = catalog;
        Jobs = jobs;
        Audit = audit;
        _ownsContexts = ownsContexts;
    }

    public OrganizationDbContext Organization { get; }

    public JudgingDbContext Judging { get; }

    public PublicationDbContext Publication { get; }

    public CatalogDbContext Catalog { get; }

    public JobsDbContext Jobs { get; }

    public AuditDbContext Audit { get; }

    public IEnumerable<DbContext> All => [Organization, Judging, Publication, Catalog, Jobs, Audit];

    // Organization
    public DbSet<BirraPoint.Api.Modules.Organization.Domain.Competition> Competitions => Organization.Competitions;

    public DbSet<BirraPoint.Api.Modules.Organization.Domain.Organizer> Organizers => Organization.Organizers;

    public DbSet<BirraPoint.Api.Modules.Organization.Domain.CompetitionCategory> CompetitionCategories => Organization.CompetitionCategories;

    public DbSet<BirraPoint.Api.Modules.Organization.Domain.Participant> Participants => Organization.Participants;

    public DbSet<BirraPoint.Api.Modules.Organization.Domain.BeerEntry> BeerEntries => Organization.BeerEntries;

    public DbSet<BirraPoint.Api.Modules.Organization.Domain.EntryCollaborator> EntryCollaborators => Organization.EntryCollaborators;

    public DbSet<BirraPoint.Api.Modules.Organization.Domain.Judge> Judges => Organization.Judges;

    public DbSet<BirraPoint.Api.Modules.Organization.Domain.Invitation> Invitations => Organization.Invitations;

    // Judging
    public DbSet<BirraPoint.Api.Modules.Judging.Domain.TastingTable> TastingTables => Judging.TastingTables;

    public DbSet<BirraPoint.Api.Modules.Judging.Domain.TableJudge> TableJudges => Judging.TableJudges;

    public DbSet<BirraPoint.Api.Modules.Judging.Domain.TableSample> TableSamples => Judging.TableSamples;

    public DbSet<BirraPoint.Api.Modules.Judging.Domain.Evaluation> Evaluations => Judging.Evaluations;

    public DbSet<BirraPoint.Api.Modules.Judging.Domain.DiscrepancyAlert> DiscrepancyAlerts => Judging.DiscrepancyAlerts;

    // Publication, catalog, building blocks
    public DbSet<BirraPoint.Api.Modules.Publication.Domain.GeneratedScoreSheet> GeneratedScoreSheets => Publication.GeneratedScoreSheets;

    public DbSet<BirraPoint.Api.Modules.Publication.Domain.ResultsArchive> ResultsArchives => Publication.ResultsArchives;

    public DbSet<BirraPoint.Api.Modules.Catalog.Domain.BjcpStyle> BjcpStyles => Catalog.BjcpStyles;

    public DbSet<BuildingBlocks.Jobs.Job> DispatchJobs => Jobs.DispatchJobs;

    public DbSet<BuildingBlocks.Audit.AuditLog> AuditLogs => Audit.AuditLogs;

    public static TestDb From(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new(
            services.GetRequiredService<OrganizationDbContext>(),
            services.GetRequiredService<JudgingDbContext>(),
            services.GetRequiredService<PublicationDbContext>(),
            services.GetRequiredService<CatalogDbContext>(),
            services.GetRequiredService<JobsDbContext>(),
            services.GetRequiredService<AuditDbContext>(),
            ownsContexts: false);
    }

    /// <summary>Contexts of their own over <paramref name="connectionString"/>, outside any host.</summary>
    public static TestDb Connect(string connectionString) => new(
        new OrganizationDbContext(Options<OrganizationDbContext>(connectionString)),
        new JudgingDbContext(Options<JudgingDbContext>(connectionString)),
        new PublicationDbContext(Options<PublicationDbContext>(connectionString)),
        new CatalogDbContext(Options<CatalogDbContext>(connectionString)),
        new JobsDbContext(Options<JobsDbContext>(connectionString)),
        new AuditDbContext(Options<AuditDbContext>(connectionString)),
        ownsContexts: true);

    /// <summary>Applies every context's migrations in the order the host does.</summary>
    public static async Task MigrateAsync(string connectionString)
    {
        await using var db = Connect(connectionString);
        foreach (var context in new DbContext[] { db.Jobs, db.Audit, db.Catalog, db.Organization, db.Judging, db.Publication })
        {
            await context.Database.MigrateAsync();
        }
    }

    public void Add(object entity) => ContextOf(entity.GetType()).Add(entity);

    public void AddRange(params object[] entities)
    {
        foreach (var entity in entities)
        {
            Add(entity);
        }
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var saved = 0;
        foreach (var context in All)
        {
            if (context.ChangeTracker.HasChanges())
            {
                saved += await context.SaveChangesAsync(cancellationToken);
            }
        }

        return saved;
    }

    private DbContext ContextOf(Type entityType) =>
        All.FirstOrDefault(c => c.Model.FindEntityType(entityType) is not null)
        ?? throw new InvalidOperationException($"No context maps {entityType.Name}.");

    private static DbContextOptions<T> Options<T>(string connectionString) where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseNpgsql(connectionString).Options;

    public async ValueTask DisposeAsync()
    {
        if (!_ownsContexts)
        {
            return;
        }

        foreach (var context in All)
        {
            await context.DisposeAsync();
        }
    }
}

internal static class TestDbExtensions
{
    public static TestDb GetTestDb(this IServiceProvider services) => TestDb.From(services);
}
