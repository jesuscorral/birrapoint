using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging.Tests;

/// <summary>Modules declare their own events next to their other registrations, in any order.</summary>
public sealed class MessagingServiceCollectionTests
{
    private static IConfigurationSection Section() =>
        new ConfigurationBuilder().AddInMemoryCollection().Build().GetSection("Messaging");

    [Fact]
    public void RegistrationsFromSeveralCallsFormOneRegistryInAnyOrder()
    {
        var services = new ServiceCollection();
        services.ConfigureMessaging(m => m.Publishes<ThingHappened>("Orders"));
        services.AddMessaging<TestDb>(Section(), m => m.Subscribe<ThingHappened, FirstHandler>("Shipping"));
        services.ConfigureMessaging(m => m.Publishes<OtherHappened>("Billing"));

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<MessagingRegistry>();

        Assert.Equal(["Billing", "Orders"], registry.GetOutboxNames());
        Assert.Equal(["Shipping"], registry.GetInboxNames());
        Assert.Equal("FirstHandler", Assert.Single(registry.Subscriptions).HandlerName);
    }

    [Fact]
    public void SubscribedHandlersAreRegisteredAsScoped()
    {
        var services = new ServiceCollection();
        services.AddMessaging<TestDb>(Section(), m => m.Subscribe<ThingHappened, FirstHandler>("Shipping"));

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(FirstHandler));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void TheDispatcherIsHostedEvenWithoutRegisteredEvents()
    {
        var services = new ServiceCollection();
        services.AddMessaging<TestDb>(Section(), _ => { });

        Assert.Contains(services, d => d.ImplementationType == typeof(OutboxDispatcher<TestDb>));
    }
}
