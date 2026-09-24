using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BirraPoint.Api.Common.Email;
using BirraPoint.Api.Common.Keycloak;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace BirraPoint.Api.IntegrationTests.Persistence;

/// <summary>
/// T095-T097: hosts the API against a deliberately *unmigrated* PostgreSQL container, in a
/// non-Development environment with Database:MigrateOnStartup=true — unlike <see cref="ApiFactory"/>,
/// which pre-migrates its container itself in InitializeAsync and would mask a regression in
/// Program.cs's own startup-migration path. This factory proves that path runs for real in the
/// production topology (Terraform sets Database:MigrateOnStartup=true on the single long-running
/// API replica, which migrates against Neon on every start), including the BJCP seed that ships
/// as migration data.
/// </summary>
public sealed class MigrateOnStartupApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Any non-Development name exercises the same branch as a real deployment; Production
        // mirrors what the ACA revision actually sets ASPNETCORE_ENVIRONMENT to.
        builder.UseEnvironment("Production");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:db"] = _container.GetConnectionString(),
                ["Database:MigrateOnStartup"] = "true",
                ["Frontend:BaseUrl"] = "http://localhost:4200",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Same test doubles as ApiFactory (T018/T039/T040/T041) — no real Keycloak/SMTP in
            // this environment either; only the startup-migration and forwarded-headers behavior
            // under test differs from ApiFactory.
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

public sealed class StartupMigrationApiTests(MigrateOnStartupApiFactory factory) : IClassFixture<MigrateOnStartupApiFactory>
{
    [Fact]
    public async Task Database_MigrateOnStartup_applies_the_schema_and_the_bjcp_seed_on_a_fresh_database()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub: "kc-user-startup-migration"));

        var response = await client.GetAsync("/api/v1/styles", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        // BJCP 2021: categories 1-34 + Appendix B (T010) — a non-empty, fully-seeded catalog is
        // only possible if Program.cs's own migration path (not this factory) created the schema.
        Assert.Equal(125, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Forwarded_https_proto_over_plain_http_is_not_redirected_and_emits_hsts()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/styles");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "birrapoint.example");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub: "kc-user-forwarded-proto"));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // No UseHttpsRedirection in this topology (nginx terminates plain HTTP internally; ACA
        // ingress terminates TLS externally) — a 307/308 here would mean it crept back in.
        Assert.NotEqual(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // UseHsts only writes Strict-Transport-Security when Request.IsHttps is true, which is
        // only the case here because UseForwardedHeaders honored X-Forwarded-Proto and rewrote
        // Request.Scheme — this is the assertion that actually pins UseForwardedHeaders being
        // wired up (unlike the redirect check above, which would stay green even without it,
        // since there's no UseHttpsRedirection in this topology either way).
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Missing_forwarded_proto_header_does_not_emit_hsts()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/styles");
        // No X-Forwarded-Proto here: Request.Scheme stays "http", so UseHsts must not fire.
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub: "kc-user-no-forwarded-proto"));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }
}

/// <summary>
/// PR follow-up: proves <see cref="StartupMigrations.ResolveMigrationConnectionString"/>'s
/// dbDirect-over-db precedence end to end. ConnectionStrings:db points at a closed local port
/// (stand-in for Neon's pooled endpoint, which breaks EF's session-scoped migration advisory lock
/// under pgbouncer transaction pooling — the reason dbDirect exists at all) while
/// ConnectionStrings:dbDirect points at the real Testcontainers database. If Program.cs's
/// migration path ever regressed to preferring ConnectionStrings:db, the migration would throw
/// against the unreachable endpoint and this factory would fail to start.
/// </summary>
public sealed class MigrateOnStartupViaDbDirectApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>The real, reachable connection string — for the test to verify the outcome of the
    /// migration directly, bypassing the API's own runtime "db" string (which stays unreachable).</summary>
    public string DirectConnectionString => _container.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Closed port, short timeout: what the runtime ("db") path would use to serve
                // requests, standing in for Neon's pooled endpoint. The migration path must never
                // touch this one — only ConnectionStrings:dbDirect below.
                ["ConnectionStrings:db"] =
                    "Host=127.0.0.1;Port=1;Database=unreachable;Username=unreachable;Password=unreachable;Timeout=1",
                ["ConnectionStrings:dbDirect"] = _container.GetConnectionString(),
                ["Database:MigrateOnStartup"] = "true",
                ["Frontend:BaseUrl"] = "http://localhost:4200",
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

public sealed class StartupMigrationViaDbDirectApiTests(MigrateOnStartupViaDbDirectApiFactory factory)
    : IClassFixture<MigrateOnStartupViaDbDirectApiFactory>
{
    [Fact]
    public async Task Database_MigrateOnStartup_migrates_over_dbDirect_when_db_is_unreachable()
    {
        // Accessing Server is what makes WebApplicationFactory actually build and start the host
        // (running Program.cs's startup-migration branch) — deliberately not issuing any HTTP
        // request here, since the request pipeline's own DbContext resolves ConnectionStrings:db,
        // which this factory leaves unreachable on purpose. DispatchWorker (a hosted
        // BackgroundService) will also try that same unreachable "db" in the background once the
        // host starts; RunGuardedAsync catches and logs that, so it must not fail host startup or
        // this assertion.
        _ = factory.Server;

        await using var direct = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(factory.DirectConnectionString)
            .Options);

        var applied = await direct.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(applied);

        // Same BJCP seed count as StartupMigrationApiTests — confirms the schema-creating
        // migration actually ran against dbDirect, not merely that some earlier migration state
        // pre-existed.
        var styleCount = await direct.BjcpStyles.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(125, styleCount);
    }
}
