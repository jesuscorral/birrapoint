using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Messaging;

/// <summary>Registers which events the host publishes (and through which module outbox) and which handlers
/// subscribe (and record in which module inbox).</summary>
public sealed class MessagingBuilder
{
    private readonly IServiceCollection _services;

    internal MessagingBuilder()
        : this(new MessagingRegistry(), new ServiceCollection())
    {
    }

    internal MessagingBuilder(MessagingRegistry registry, IServiceCollection services)
    {
        Registry = registry;
        _services = services;
    }

    internal MessagingRegistry Registry { get; }

    /// <summary>The event goes to the outbox <paramref name="outbox"/>, the name given to
    /// <see cref="MessagingModelBuilderExtensions.ApplyOutbox"/>. An event has exactly one publisher.</summary>
    public MessagingBuilder Publishes<TEvent>(string outbox)
        where TEvent : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outbox);
        Registry.AddPublisher(typeof(TEvent), outbox);
        return this;
    }

    /// <summary>
    /// <typeparamref name="THandler"/> (registered as scoped) handles <typeparamref name="TEvent"/>, recording
    /// processed ids in the inbox <paramref name="inbox"/>, the name given to
    /// <see cref="MessagingModelBuilderExtensions.ApplyInbox"/>. The inbox key uses the handler's short type name.
    /// </summary>
    public MessagingBuilder Subscribe<TEvent, THandler>(string inbox)
        where TEvent : class
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inbox);
        Registry.AddSubscription(new Subscription(
            Registry.Register(typeof(TEvent)),
            typeof(THandler),
            typeof(THandler).Name,
            inbox,
            (handler, evt, context, cancellationToken) =>
                ((THandler)handler).HandleAsync((TEvent)evt, context, cancellationToken)));
        _services.TryAddScoped<THandler>();
        return this;
    }
}
