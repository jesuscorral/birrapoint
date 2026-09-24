using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BirraPoint.Api.Common.Email;
using BirraPoint.Api.Common.Keycloak;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
/// production topology (ACA's one-off migration run against Neon), including the BJCP seed that
/// ships as migration data.
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
    public async Task Forwarded_https_proto_over_plain_http_is_not_redirected_in_a_reverse_proxy_topology()
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
    }
}
