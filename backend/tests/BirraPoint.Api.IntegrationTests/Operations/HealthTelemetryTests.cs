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
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires fixtures used in public test-class constructors (IClassFixture<T>) to be public.")]
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
        ArgumentNullException.ThrowIfNull(builder);
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
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires fixtures used in public test-class constructors (IClassFixture<T>) to be public.")]
public sealed class ProductionDbDownApiFactory : WebApplicationFactory<Program>
{
    // Distinctive values so the leak assertions can look for them in response bodies.
    public const string LeakCanaryHost = "127.0.0.1";
    public const string LeakCanaryPassword = "canary-secret-pw";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
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

/// <summary>Host that, like Aspire, supplies service.instance.id through OTEL_RESOURCE_ATTRIBUTES.
/// No container needed: the telemetry resource is built without touching the database.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires fixtures used in public test-class constructors (IClassFixture<T>) to be public.")]
public sealed class AspireResourceAttributesApiFactory : WebApplicationFactory<Program>
{
    public const string InstanceId = "aspire-instance-abc";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:db"] = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1",
                ["Frontend:BaseUrl"] = "http://localhost:4200",
                ["Keycloak:AdminClientSecret"] = "integration-test-secret",
                ["OTEL_RESOURCE_ATTRIBUTES"] = $"service.instance.id={InstanceId}",
            }));
    }
}

public sealed class HealthEndpointsProductionTests(ProductionHealthyApiFactory factory)
    : IClassFixture<ProductionHealthyApiFactory>
{
    [Theory]
    [InlineData("/alive")]
    [InlineData("/health")]
    public async Task ProbeIsMappedInProductionAnonymousAndReturnsBareHealthy(string path)
    {
        using var client = factory.CreateClient(); // no bearer: fallback policy must not block probes

        var response = await client.GetAsync(Urls.Rel(path), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task HealthBodyDoesNotLeakCheckNamesOrConnectionDetails()
    {
        using var client = factory.CreateClient();

        var body = await client.GetStringAsync(Urls.Rel("/health"), TestContext.Current.CancellationToken);

        Assert.Equal("Healthy", body);
        Assert.DoesNotContain("self", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"db\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=", body, StringComparison.Ordinal);
    }
}

public sealed class HealthEndpointsDbDownTests : IClassFixture<ProductionDbDownApiFactory>
{
    private readonly ProductionDbDownApiFactory _factory;

    public HealthEndpointsDbDownTests(ProductionDbDownApiFactory factory) => _factory = factory;

    [Fact]
    public async Task HealthReturns503UnhealthyWhenDatabaseIsUnreachableWithoutLeakingDetails()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Urls.Rel("/health"), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", body);
        Assert.DoesNotContain(ProductionDbDownApiFactory.LeakCanaryPassword, body, StringComparison.Ordinal);
        Assert.DoesNotContain(ProductionDbDownApiFactory.LeakCanaryHost, body, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AliveStays200WhenDatabaseIsUnreachableBecauseItRunsOnlyLiveChecks()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Urls.Rel("/alive"), TestContext.Current.CancellationToken);
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
    public async Task ProbeReturns200HealthyAnonymously(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Urls.Rel(path), TestContext.Current.CancellationToken);

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
    public void TracerProviderAndMeterProviderAreRegistered()
    {
        Assert.NotNull(factory.Services.GetService<TracerProvider>());
        Assert.NotNull(factory.Services.GetService<MeterProvider>());
    }

    [Fact]
    public void OpenTelemetryLoggerProviderIsRegistered()
    {
        var providers = factory.Services.GetServices<ILoggerProvider>();

        Assert.Contains(providers, p => p is OpenTelemetryLoggerProvider);
    }

    [Fact]
    public void ResourceServiceNameIsExplicitlySetNotTheUnknownServiceDefault()
    {
        // ServiceDefaults only reads OTEL_SERVICE_NAME (injected by Aspire). Outside Aspire (ACA/ECS)
        // nothing sets it and the SDK falls back to "unknown_service:<process>", so the app must
        // configure a service name itself. Exact value left to the implementation.
        var serviceName = factory.Services.GetRequiredService<TracerProvider>()
            .GetResource().Attributes
            .Single(a => a.Key == "service.name").Value as string;

        Assert.False(string.IsNullOrWhiteSpace(serviceName));
        Assert.False(serviceName.StartsWith("unknown_service", StringComparison.Ordinal),
            $"service.name was '{serviceName}'");
    }
}

public sealed class TelemetryResourceInstanceIdTests(AspireResourceAttributesApiFactory factory)
    : IClassFixture<AspireResourceAttributesApiFactory>
{
    [Fact]
    public void ExplicitServiceNameDoesNotOverwriteServiceInstanceIdFromOTELRESOURCEATTRIBUTES()
    {
        var instanceId = factory.Services.GetRequiredService<TracerProvider>()
            .GetResource().Attributes
            .Single(a => a.Key == "service.instance.id").Value as string;

        Assert.Equal(AspireResourceAttributesApiFactory.InstanceId, instanceId);
    }
}
