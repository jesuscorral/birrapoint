using BirraPoint.Api.IntegrationTests.TestHost;
using BuildingBlocks.EmailSender;
using BuildingBlocks.Keycloak.Admin;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BirraPoint.Api.IntegrationTests.Persistence;

/// <summary>
/// Production-shaped host (<c>Database:MigrateOnStartup</c>) over an empty PostgreSQL container, optionally
/// prepared by <see cref="SeedSql"/> before the host starts.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires fixtures used in public test-class constructors (IClassFixture<T>) to be public.")]
public abstract class StartupMigrationApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public string ConnectionString => _container.GetConnectionString();

    protected virtual string? SeedSql => null;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Test-only statements built from constants; no user input.")]
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        if (SeedSql is not null)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(SeedSql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Production");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:db"] = ConnectionString,
                ["Database:MigrateOnStartup"] = "true",
                ["Frontend:BaseUrl"] = "http://localhost:4200",
                ["Keycloak:AdminClientSecret"] = "integration-test-secret",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.TokenValidationParameters = TestJwtIssuer.ValidationParameters;
            });

            services.RemoveAll<IKeycloakAdminClient>();
            services.AddSingleton<IKeycloakAdminClient, FakeKeycloakAdminClient>();

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, FakeEmailSender>();
        });
    }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires fixtures used in public test-class constructors (IClassFixture<T>) to be public.")]
public sealed class EmptyDatabaseApiFactory : StartupMigrationApiFactory;

/// <summary>A database built by the single shared history of the previous layout: <c>migrations."__EFMigrationsHistory"</c>.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires fixtures used in public test-class constructors (IClassFixture<T>) to be public.")]
public sealed class SharedHistoryDatabaseApiFactory : StartupMigrationApiFactory
{
    protected override string? SeedSql =>
        """
        CREATE SCHEMA migrations;
        CREATE TABLE migrations."__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
        INSERT INTO migrations."__EFMigrationsHistory" VALUES ('20261108203847_InitialCreate', '10.0.0');
        """;
}

/// <summary>
/// Every store migrates on its own (FR-097, FR-098): after startup on an empty database each schema owns an
/// <c>__EFMigrationsHistory</c> with its single <c>InitialCreate</c> baseline, the shared <c>migrations</c> schema
/// is gone and the BJCP catalog is seeded; a second start applies nothing; a database that still carries the
/// shared history is refused with the reset instruction.
/// </summary>
public sealed class StartupMigrationTests(EmptyDatabaseApiFactory empty, SharedHistoryDatabaseApiFactory shared)
    : IClassFixture<EmptyDatabaseApiFactory>, IClassFixture<SharedHistoryDatabaseApiFactory>
{
    private static readonly string[] Schemas = ["jobs", "audit", "catalog", "organization", "judging", "publication"];

    [Fact]
    public async Task StartupOnAnEmptyDatabaseGivesEverySchemaItsOwnHistoryWithOneBaselineAndSeedsTheCatalog()
    {
        _ = empty.Server;

        foreach (var schema in Schemas)
        {
            var applied = await QueryAsync(
                empty.ConnectionString, $"""SELECT "MigrationId" FROM {schema}."__EFMigrationsHistory" ORDER BY "MigrationId" """);
            var migration = Assert.Single(applied);
            Assert.EndsWith("_InitialCreate", migration, StringComparison.Ordinal);
        }

        var migrationsSchema = await QueryAsync(
            empty.ConnectionString, "SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'migrations'");
        Assert.Empty(migrationsSchema);

        var publicHistory = await QueryAsync(
            empty.ConnectionString, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '__EFMigrationsHistory'");
        Assert.Empty(publicHistory);

        var styles = await QueryAsync(empty.ConnectionString, """SELECT count(*)::text FROM catalog."BjcpStyles" """);
        Assert.Equal("125", Assert.Single(styles));
    }

    [Fact]
    public async Task ASecondStartupAppliesNothing()
    {
        _ = empty.Server;
        var before = await HistorySnapshotAsync(empty.ConnectionString);

        await using var second = empty.WithWebHostBuilder(_ => { });
        _ = second.Server;

        Assert.Equal(before, await HistorySnapshotAsync(empty.ConnectionString));
        Assert.Equal(Schemas.Length, before.Count);
    }

    [Fact]
    public async Task StartupOnADatabaseWithTheSharedHistoryFailsNamingTheResetCommandAndCreatesNothing()
    {
        var ex = Record.Exception(() => _ = shared.Server);

        Assert.NotNull(ex);
        Assert.Contains("Reset database", Flatten(ex), StringComparison.Ordinal);

        var created = await QueryAsync(
            shared.ConnectionString,
            "SELECT table_schema || '.' || table_name FROM information_schema.tables " +
            "WHERE table_schema IN ('catalog', 'organization', 'judging', 'publication', 'jobs', 'audit')");
        Assert.Empty(created);
    }

    private static async Task<List<string>> HistorySnapshotAsync(string connectionString)
    {
        var snapshot = new List<string>();
        foreach (var schema in Schemas)
        {
            var rows = await QueryAsync(
                connectionString, $"""SELECT "MigrationId" FROM {schema}."__EFMigrationsHistory" ORDER BY "MigrationId" """);
            snapshot.Add($"{schema}:{string.Join(",", rows)}");
        }

        return snapshot;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Test-only statements built from constants; no user input.")]
    private static async Task<List<string>> QueryAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static string Flatten(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }
}
