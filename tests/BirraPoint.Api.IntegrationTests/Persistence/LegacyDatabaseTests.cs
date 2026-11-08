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
/// Hosts the API against a database built from the pre-baseline migrations: its history lives in
/// <c>public."__EFMigrationsHistory"</c> with an old migration id (FR-070).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires fixtures used in public test-class constructors (IClassFixture<T>) to be public.")]
public sealed class LegacyDatabaseApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            CREATE TABLE public."__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
            INSERT INTO public."__EFMigrationsHistory" VALUES ('20260708211914_InitialCreate', '10.0.0');
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
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

public sealed class LegacyDatabaseTests(LegacyDatabaseApiFactory factory) : IClassFixture<LegacyDatabaseApiFactory>
{
    // FR-070
    [Fact]
    public async Task StartupOnADatabaseWithTheOldMigrationHistoryFailsNamingTheResetCommandAndCreatesNothing()
    {
        var ex = Record.Exception(() => _ = factory.Server);

        Assert.NotNull(ex);
        Assert.Contains("Reset database", Flatten(ex), StringComparison.Ordinal);

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables " +
            "WHERE table_schema IN ('catalog', 'organization', 'judging', 'publication', 'jobs', 'audit', 'migrations')",
            connection);
        var created = (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;

        Assert.Equal(0, created);
    }

    private static string Flatten(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
            if (current.InnerException is null)
            {
                break;
            }
        }

        return string.Join(" | ", messages);
    }
}
