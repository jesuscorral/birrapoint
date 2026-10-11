using System.Net;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Testcontainers.PostgreSql;

namespace BirraPoint.Api.IntegrationTests.Operations;

/// <summary>
/// T098 (FR-048): Production host against a reachable Postgres Testcontainer (no migration needed:
/// the "db" readiness check only opens a connection).
/// </summary>
public sealed class ProductionHealthyApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
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
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:db"] = _container.GetConnectionString(),
                ["Frontend:BaseUrl"] = "http://localhost:4200",
                ["Keycloak:AdminClientSecret"] = "integration-test-secret",
            }));
    }
}

/// <summary>Production host whose database is deterministically unreachable: 127.0.0.1 port 1 is
/// closed (immediate connection refusal), Timeout=1 bounds any retry. No container needed.</summary>
public sealed class ProductionDbDownApiFactory : WebApplicationFactory<Program>
{
    // Distinctive values so the leak assertions can look for them in response bodies.
    public const string LeakCanaryHost = "127.0.0.1";
    public const string LeakCanaryPassword = "canary-secret-pw";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:db"] =
                    $"Host={LeakCanaryHost};Port=1;Database=unreachable;Username=unreachable;Password={LeakCanaryPassword};Timeout=1",
                ["Frontend:BaseUrl"] = "http://localhost:4200",
                ["Keycloak:AdminClientSecret"] = "integration-test-secret",
            }));
    }
}

public sealed class HealthEndpointsProductionTests(ProductionHealthyApiFactory factory)
    : IClassFixture<ProductionHealthyApiFactory>
{
    [Theory]
    [InlineData("/alive")]
    [InlineData("/health")]
    public async Task Probe_is_mapped_in_Production_anonymous_and_returns_bare_Healthy(string path)
    {
        using var client = factory.CreateClient(); // no bearer: fallback policy must not block probes

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task Health_body_does_not_leak_check_names_or_connection_details()
    {
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal("Healthy", body);
        Assert.DoesNotContain("self", body);
        Assert.DoesNotContain("\"db\"", body);
        Assert.DoesNotContain("Host=", body);
    }
}

public sealed class HealthEndpointsDbDownTests : IClassFixture<ProductionDbDownApiFactory>
{
    private readonly ProductionDbDownApiFactory _factory;

    public HealthEndpointsDbDownTests(ProductionDbDownApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_returns_503_Unhealthy_when_database_is_unreachable_without_leaking_details()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", body);
        Assert.DoesNotContain(ProductionDbDownApiFactory.LeakCanaryPassword, body);
        Assert.DoesNotContain(ProductionDbDownApiFactory.LeakCanaryHost, body);
        Assert.DoesNotContain("Npgsql", body);
        Assert.DoesNotContain("Exception", body);
    }

    [Fact]
    public async Task Alive_stays_200_when_database_is_unreachable_because_it_runs_only_live_checks()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/alive", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body);
    }
}

/// <summary>Default Testing environment (reachable Testcontainer): same contract as Production.</summary>
public sealed class HealthEndpointsTestingEnvTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/alive")]
    [InlineData("/health")]
    public async Task Probe_returns_200_Healthy_anonymously(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}

/// <summary>
/// T098 telemetry wiring, asserted from DI (no exporter / no new packages). Not covered, by design:
/// an HTTP request producing an ASP.NET Core Activity captured by the TracerProvider — without an
/// in-memory exporter package, a test ActivityListener would itself force the framework to create
/// the Activity, so the assertion would pass even with instrumentation missing (never red).
/// </summary>
public sealed class TelemetryRegistrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void TracerProvider_and_MeterProvider_are_registered()
    {
        Assert.NotNull(factory.Services.GetService<TracerProvider>());
        Assert.NotNull(factory.Services.GetService<MeterProvider>());
    }

    [Fact]
    public void OpenTelemetry_logger_provider_is_registered()
    {
        var providers = factory.Services.GetServices<ILoggerProvider>();

        Assert.Contains(providers, p => p is OpenTelemetryLoggerProvider);
    }

    [Fact]
    public void Resource_service_name_is_explicitly_set_not_the_unknown_service_default()
    {
        // ServiceDefaults only reads OTEL_SERVICE_NAME (injected by Aspire). Outside Aspire (ACA/ECS)
        // nothing sets it and the SDK falls back to "unknown_service:<process>", so the app must
        // configure a service name itself. Exact value left to the implementation.
        var serviceName = factory.Services.GetRequiredService<TracerProvider>()
            .GetResource().Attributes
            .Single(a => a.Key == "service.name").Value as string;

        Assert.False(string.IsNullOrWhiteSpace(serviceName));
        Assert.False(serviceName!.StartsWith("unknown_service", StringComparison.Ordinal),
            $"service.name was '{serviceName}'");
    }
}
