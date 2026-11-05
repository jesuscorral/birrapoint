using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Audit.Tests;

public class AuditServiceCollectionTests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by the DI container through AddAudit.")]
    private sealed class HostActor : IAuditActorAccessor
    {
        public string ActorUserId => "host-actor";
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(options => options.UseNpgsql("Host=unused"));
        extra?.Invoke(services);
        services.AddAudit<TestDbContext, HostActor>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void RegistersAScopedWriterOverTheHostContext()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAuditWriter>();

        Assert.IsType<AuditWriter<TestDbContext>>(writer);
        Assert.Same(writer, scope.ServiceProvider.GetRequiredService<IAuditWriter>());
        Assert.IsType<HostActor>(scope.ServiceProvider.GetRequiredService<IAuditActorAccessor>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void KeepsAHostRegisteredTimeProvider()
    {
        var hostClock = new FixedTimeProvider(DateTimeOffset.UnixEpoch);
        using var provider = BuildProvider(services => services.AddSingleton<TimeProvider>(hostClock));

        Assert.Same(hostClock, provider.GetRequiredService<TimeProvider>());
    }
}
