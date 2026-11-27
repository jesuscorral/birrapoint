using BirraPoint.Api.Common.Persistence.Audit;
using BirraPoint.Api.Common.Persistence.Jobs;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Catalog.Persistence;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Judging.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts.IntegrationEvents;
using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Organization.Persistence;
using BirraPoint.Api.Modules.Publication.Persistence;
using BuildingBlocks.Audit;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace BirraPoint.Api.IntegrationTests.Persistence;

/// <summary>
/// One request scope shares one connection and one transaction across every data context (FR-096): a single
/// <c>SaveChangesAsync</c> on a module context commits the module rows together with the audit entry, the staged
/// job and the outbox event, and a failure or rollback leaves none of them. Rows are inspected over a separate
/// connection, so only committed data counts. The host runs without the outbox dispatcher so the staged event
/// stays where the publisher put it.
/// </summary>
public sealed class SharedTransactionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ProbeJobType = "shared-tx-probe";

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by the DI container.")]
    private sealed class FixedAuditActor : IAuditActorAccessor
    {
        public string ActorUserId => "shared-transaction-test";
    }

    /// <summary>The four writes of one unit of work, identified by values no other test uses.</summary>
    private sealed record Staged(string OrganizerEmail, string AuditEntityId, Guid JobKey, Guid EventId);

    private sealed record Counts(long Organizers, long AuditLogs, long Jobs, long OutboxMessages)
    {
        public static readonly Counts None = new(0, 0, 0, 0);

        public static readonly Counts All = new(1, 1, 1, 1);
    }

    private WebApplicationFactory<Program> NewHost() =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Messaging:DispatcherEnabled"] = "false" }));
            builder.ConfigureServices(services =>
            {
                // The real accessor reads the HTTP caller; there is no request here.
                services.RemoveAll<IAuditActorAccessor>();
                services.AddSingleton<IAuditActorAccessor, FixedAuditActor>();
            });
        });

    /// <summary>Stages a module row, an audit entry, a job and an outbox event; saves nothing.</summary>
    private static Staged StageEverything(IServiceProvider services, OrganizationDbContext organization, string? organizerEmail = null)
    {
        var email = organizerEmail ?? $"shared-{Guid.NewGuid():N}@brew.example";
        var staged = new Staged(email, Guid.NewGuid().ToString("N"), Guid.NewGuid(), Guid.Empty);

        organization.Organizers.Add(NewOrganizer(email));
        services.GetRequiredService<IAuditWriter>().Record("SharedTransactionProbe", "Organizer", staged.AuditEntityId);
        services.GetRequiredService<IJobQueue>().Stage(staged.JobKey, ProbeJobType, new { probe = true });
        var eventId = services.GetRequiredService<IIntegrationEventPublisher>()
            .Publish(new JudgeEmailChanged(Guid.NewGuid(), Guid.NewGuid()));
        return staged with { EventId = eventId };
    }

    private static Organizer NewOrganizer(string email) => new()
    {
        KeycloakUserId = $"kc-{Guid.NewGuid():N}",
        Email = email,
        FirstName = "Shared",
        LastName = "Transaction",
    };

    private async Task<Counts> CountAsync(Staged staged)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return new Counts(
            await ScalarAsync(connection, """SELECT count(*) FROM organization."Organizers" WHERE "Email" = @v""", staged.OrganizerEmail),
            await ScalarAsync(connection, """SELECT count(*) FROM audit."AuditLogs" WHERE "EntityId" = @v""", staged.AuditEntityId),
            await ScalarAsync(connection, """SELECT count(*) FROM jobs."DispatchJobs" WHERE "CompetitionId" = @v""", staged.JobKey),
            await ScalarAsync(connection, """SELECT count(*) FROM organization."OutboxMessages" WHERE "Id" = @v""", staged.EventId));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Test-only statements built from constants; no user input.")]
    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql, object value)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("v", value);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task OneModuleSaveCommitsTheModuleRowTheAuditEntryTheStagedJobAndTheOutboxEvent()
    {
        await using var host = NewHost();
        await using var scope = host.Services.CreateAsyncScope();
        var organization = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        var staged = StageEverything(scope.ServiceProvider, organization);

        await organization.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Counts.All, await CountAsync(staged));
    }

    [Fact]
    public async Task AFailingModuleSaveLeavesNoneOfTheFourWrites()
    {
        await using var host = NewHost();
        var taken = $"taken-{Guid.NewGuid():N}@brew.example";
        await using (var seedScope = host.Services.CreateAsyncScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            seed.Organizers.Add(NewOrganizer(taken));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var organization = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        // The organizer email is unique, so this save violates the index after the other three are staged.
        var staged = StageEverything(scope.ServiceProvider, organization, organizerEmail: taken);

        await Assert.ThrowsAsync<DbUpdateException>(() => organization.SaveChangesAsync(TestContext.Current.CancellationToken));

        var counts = await CountAsync(staged);
        Assert.Equal(1, counts.Organizers); // only the pre-existing row
        Assert.Equal(0, counts.AuditLogs);
        Assert.Equal(0, counts.Jobs);
        Assert.Equal(0, counts.OutboxMessages);
    }

    [Fact]
    public async Task RollingBackAnExplicitTransactionOnTheModuleContextLeavesNoneOfTheFourWrites()
    {
        await using var host = NewHost();
        await using var scope = host.Services.CreateAsyncScope();
        var organization = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        var staged = StageEverything(scope.ServiceProvider, organization);

        await using (var transaction = await organization.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await organization.SaveChangesAsync(TestContext.Current.CancellationToken);
            Assert.Equal(Counts.None, await CountAsync(staged)); // uncommitted rows are invisible elsewhere
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(Counts.None, await CountAsync(staged));
    }

    [Fact]
    public async Task CommittingAnExplicitTransactionOnTheModuleContextPublishesAllFourWritesTogether()
    {
        await using var host = NewHost();
        await using var scope = host.Services.CreateAsyncScope();
        var organization = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        var staged = StageEverything(scope.ServiceProvider, organization);

        await using (var transaction = await organization.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await organization.SaveChangesAsync(TestContext.Current.CancellationToken);
            Assert.Equal(Counts.None, await CountAsync(staged));
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(Counts.All, await CountAsync(staged));
    }

    [Fact]
    public async Task ATransactionBegunOnTheJobsContextCommitsTheModuleHandlerWritesWithTheJobState()
    {
        await using var host = NewHost();
        await using var scope = host.Services.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
        var organization = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        var jobKey = Guid.NewGuid();
        var email = $"worker-{Guid.NewGuid():N}@brew.example";
        var staged = new Staged(email, Guid.NewGuid().ToString("N"), jobKey, Guid.Empty);

        await using (var transaction = await jobs.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            var job = new Job { GroupKey = jobKey, Type = ProbeJobType, PayloadJson = "{}", Status = JobStatus.Running };
            jobs.Set<Job>().Add(job);
            await jobs.SaveChangesAsync(TestContext.Current.CancellationToken);

            // What a job handler does: write through its own module context inside the worker's transaction.
            organization.Organizers.Add(NewOrganizer(email));
            await organization.SaveChangesAsync(TestContext.Current.CancellationToken);

            job.Status = JobStatus.Completed;
            await jobs.SaveChangesAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, (await CountAsync(staged)).Organizers);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        var counts = await CountAsync(staged);
        Assert.Equal(1, counts.Organizers);
        Assert.Equal(1, counts.Jobs);
        await using var verify = host.Services.CreateAsyncScope();
        var stored = await verify.ServiceProvider.GetRequiredService<JobsDbContext>().Set<Job>()
            .AsNoTracking().SingleAsync(j => j.GroupKey == jobKey, TestContext.Current.CancellationToken);
        Assert.Equal(JobStatus.Completed, stored.Status);
    }

    [Fact]
    public async Task ARollbackOfATransactionBegunOnTheJobsContextDiscardsTheModuleHandlerWritesToo()
    {
        await using var host = NewHost();
        await using var scope = host.Services.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
        var organization = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        var jobKey = Guid.NewGuid();
        var email = $"worker-{Guid.NewGuid():N}@brew.example";
        var staged = new Staged(email, Guid.NewGuid().ToString("N"), jobKey, Guid.Empty);

        await using (var transaction = await jobs.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            jobs.Set<Job>().Add(new Job { GroupKey = jobKey, Type = ProbeJobType, PayloadJson = "{}" });
            await jobs.SaveChangesAsync(TestContext.Current.CancellationToken);
            organization.Organizers.Add(NewOrganizer(email));
            await organization.SaveChangesAsync(TestContext.Current.CancellationToken);
            await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        }

        var counts = await CountAsync(staged);
        Assert.Equal(0, counts.Organizers);
        Assert.Equal(0, counts.Jobs);
    }

    [Fact]
    public async Task EveryContextInAScopeUsesTheSameNpgsqlConnectionInstanceAndEachScopeHasItsOwn()
    {
        await using var host = NewHost();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var shared = services.GetRequiredService<NpgsqlConnection>();

        Assert.Same(shared, services.GetRequiredService<CatalogDbContext>().Database.GetDbConnection());
        Assert.Same(shared, services.GetRequiredService<OrganizationDbContext>().Database.GetDbConnection());
        Assert.Same(shared, services.GetRequiredService<JudgingDbContext>().Database.GetDbConnection());
        Assert.Same(shared, services.GetRequiredService<PublicationDbContext>().Database.GetDbConnection());
        Assert.Same(shared, services.GetRequiredService<JobsDbContext>().Database.GetDbConnection());
        Assert.Same(shared, services.GetRequiredService<AuditDbContext>().Database.GetDbConnection());

        await using var other = host.Services.CreateAsyncScope();
        Assert.NotSame(shared, other.ServiceProvider.GetRequiredService<NpgsqlConnection>());
    }

    [Fact]
    public async Task CreatedAtAndUpdatedAtAreStampedOnEveryContextAndUpdatedAtMovesOnChange()
    {
        await using var host = NewHost();
        var email = $"stamp-{Guid.NewGuid():N}@brew.example";
        var jobKey = Guid.NewGuid();
        Guid organizerId;
        Guid tableId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var organization = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            var judging = scope.ServiceProvider.GetRequiredService<JudgingDbContext>();
            var jobs = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
            var organizer = NewOrganizer(email);
            var table = new TastingTable { CompetitionId = Guid.NewGuid(), Name = $"Stamp {Guid.NewGuid():N}" };
            organization.Organizers.Add(organizer);
            judging.TastingTables.Add(table);
            jobs.Set<Job>().Add(new Job { GroupKey = jobKey, Type = ProbeJobType, PayloadJson = "{}" });

            await organization.SaveChangesAsync(TestContext.Current.CancellationToken);
            await judging.SaveChangesAsync(TestContext.Current.CancellationToken);
            await jobs.SaveChangesAsync(TestContext.Current.CancellationToken);
            organizerId = organizer.Id;
            tableId = table.Id;
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var organization = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            var judging = scope.ServiceProvider.GetRequiredService<JudgingDbContext>();
            var jobs = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
            var organizer = await organization.Organizers.SingleAsync(o => o.Id == organizerId, TestContext.Current.CancellationToken);
            var table = await judging.TastingTables.SingleAsync(t => t.Id == tableId, TestContext.Current.CancellationToken);
            var job = await jobs.Set<Job>().SingleAsync(j => j.GroupKey == jobKey, TestContext.Current.CancellationToken);

            Assert.NotEqual(default, organizer.CreatedAt);
            Assert.NotEqual(default, table.CreatedAt);
            Assert.NotEqual(default, job.CreatedAt);
            Assert.True(organizer.UpdatedAt >= organizer.CreatedAt);
            Assert.True(table.UpdatedAt >= table.CreatedAt);
            Assert.True(job.UpdatedAt >= job.CreatedAt);

            var organizerCreated = organizer.CreatedAt;
            var jobCreated = job.CreatedAt;
            var organizerUpdated = organizer.UpdatedAt;
            var jobUpdated = job.UpdatedAt;
            organizer.FirstName = "Changed";
            job.Attempts = 1;
            await organization.SaveChangesAsync(TestContext.Current.CancellationToken);
            await jobs.SaveChangesAsync(TestContext.Current.CancellationToken);

            Assert.Equal(organizerCreated, organizer.CreatedAt);
            Assert.Equal(jobCreated, job.CreatedAt);
            Assert.True(organizer.UpdatedAt > organizerUpdated);
            Assert.True(job.UpdatedAt > jobUpdated);
        }
    }
}
