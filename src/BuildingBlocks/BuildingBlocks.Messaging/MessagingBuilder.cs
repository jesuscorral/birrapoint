using Microsoft.EntityFrameworkCore;
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

    /// <summary>Declares the outbox <paramref name="name"/> (applied with
    /// <see cref="MessagingModelBuilderExtensions.ApplyOutbox"/>) as living in <typeparamref name="TContext"/>. The
    /// publisher stores its rows through the scoped instance of that context, so they commit with the caller's change
    /// (R-107-3). Declaring a name again for another context type throws.</summary>
    public MessagingBuilder Outbox<TContext>(string name)
        where TContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Registry.DeclareOutbox(name, typeof(TContext));
        return this;
    }

    /// <summary>Declares the inbox <paramref name="name"/> (applied with
    /// <see cref="MessagingModelBuilderExtensions.ApplyInbox"/>) as living in <typeparamref name="TContext"/>; its
    /// handlers run on that context. Declaring a name again for another context type throws.</summary>
    public MessagingBuilder Inbox<TContext>(string name)
        where TContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Registry.DeclareInbox(name, typeof(TContext));
        return this;
    }

    /// <summary>The event goes to the outbox <paramref name="outbox"/>, the name given to
    /// <see cref="MessagingModelBuilderExtensions.ApplyOutbox"/>, declared first with <c>Outbox&lt;TContext&gt;</c>. An event has exactly one publisher.</summary>
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
