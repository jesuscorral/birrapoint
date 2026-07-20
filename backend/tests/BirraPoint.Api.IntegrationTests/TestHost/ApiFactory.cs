using BirraPoint.Api.Common.Email;
using BirraPoint.Api.Common.Keycloak;
using BirraPoint.Api.Common.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace BirraPoint.Api.IntegrationTests.TestHost;

/// <summary>
/// T018: hosts the real API in-process against a dedicated PostgreSQL Testcontainer (one per
/// test class, mirrors Persistence/PostgresFixture — InitialCreate + BJCP seed applied once) and
/// swaps JWT validation to trust <see cref="TestJwtIssuer"/> instead of a live Keycloak discovery
/// round-trip.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options);
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // Stop hosted services (DispatchWorker) and drain the Npgsql pool against a live DB
        // before the container goes away, not after.
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:db"] = _container.GetConnectionString(),
                // SendInvitationHandler (T041) requires this to build the invitation email body;
                // only real AppHost env injection sets it outside this test host.
                ["Frontend:BaseUrl"] = "http://localhost:4200",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Trust TestJwtIssuer's tokens statically instead of hitting a real Keycloak
            // discovery endpoint — Authority stays unset, so no metadata network call.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.TokenValidationParameters = TestJwtIssuer.ValidationParameters;
            });

            // Judge provisioning/invitation delivery (T039/T040/T041) has no real Keycloak Admin
            // API or SMTP server in the test environment — swap in in-memory fakes. RemoveAll is
            // needed because IKeycloakAdminClient is registered via AddHttpClient<,> (a typed
            // client, not a plain AddScoped) in Program.cs.
            services.RemoveAll<IKeycloakAdminClient>();
            services.AddSingleton<IKeycloakAdminClient, FakeKeycloakAdminClient>();

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, FakeEmailSender>();
        });
    }
}
