using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the publisher, the in-process transport, the inbox runner, the retention purger and the hosted
    /// outbox dispatcher over <typeparamref name="TContext"/>, which must map every outbox and inbox named in
    /// <paramref name="configure"/> with <see cref="MessagingModelBuilderExtensions.ApplyOutbox"/> /
    /// <see cref="MessagingModelBuilderExtensions.ApplyInbox"/>. Options bind from <paramref name="section"/> and are
    /// validated on host start. Handlers are registered as scoped. To wake the dispatcher right after a commit,
    /// call <see cref="MessagingDbContextOptionsBuilderExtensions.AddMessagingInterceptors"/> in the host's
    /// <c>AddDbContext</c>. Register another <see cref="IIntegrationEventTransport"/> afterwards to replace the
    /// in-process one.
    /// </summary>
    public static IServiceCollection AddMessaging<TContext>(
        this IServiceCollection services, IConfigurationSection section, Action<MessagingBuilder> configure)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(configure);

        services.ConfigureMessaging(configure);

        services.TryAddSingleton<IValidateOptions<MessagingOptions>, MessagingOptionsValidator>();
        services.AddOptions<MessagingOptions>().Bind(section).ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<OutboxWakeUp>();
        services.TryAddSingleton<OutboxWakeUpInterceptor>();
        services.TryAddSingleton<OutboxWakeUpTransactionInterceptor>();
        services.TryAddScoped<IIntegrationEventPublisher, IntegrationEventPublisher<TContext>>();
        services.TryAddSingleton<InboxRunner<TContext>>();
        services.TryAddSingleton<IIntegrationEventTransport, LocalTransport<TContext>>();
        services.TryAddSingleton<RetentionPurger<TContext>>();
        services.AddHostedService<OutboxDispatcher<TContext>>();
        return services;
    }

    /// <summary>
    /// Adds more publish and subscribe registrations to the messaging setup, so each module can declare its own
    /// events next to its other registrations. Order-independent with <see cref="AddMessaging{TContext}"/>; the
    /// registrations of all calls form one registry.
    /// </summary>
    public static IServiceCollection ConfigureMessaging(this IServiceCollection services, Action<MessagingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var registry = services.Select(d => d.ImplementationInstance).OfType<MessagingRegistry>().FirstOrDefault();
        if (registry is null)
        {
            registry = new MessagingRegistry();
            services.AddSingleton(registry);
        }

        configure(new MessagingBuilder(registry, services));
        return services;
    }
}
