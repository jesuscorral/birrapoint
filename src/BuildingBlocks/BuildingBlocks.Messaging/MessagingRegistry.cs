using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging;

/// <summary>One handler's subscription to one event type.</summary>
internal sealed record Subscription(
    EventDescriptor Event,
    Type HandlerType,
    string HandlerName,
    string InboxName,
    Func<object, object, IntegrationEventContext, CancellationToken, Task> Invoke);

/// <summary>What the host declared in <c>AddMessaging</c>: known events, who publishes them, who handles them.</summary>
internal sealed class MessagingRegistry
{
    private readonly Dictionary<(string Type, int Version), EventDescriptor> _byKey = [];
    private readonly Dictionary<Type, EventDescriptor> _byClrType = [];
    private readonly Dictionary<Type, string> _outboxByClrType = [];
    private readonly Dictionary<string, Type> _outboxContexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _inboxContexts = new(StringComparer.Ordinal);
    private readonly List<Subscription> _subscriptions = [];

    public IReadOnlyList<Subscription> Subscriptions => _subscriptions;

    /// <summary>Declared outboxes the dispatcher sweeps and the purger cleans.</summary>
    public IReadOnlyList<string> GetOutboxNames() => _outboxContexts.Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>Declared inboxes the purger cleans.</summary>
    public IReadOnlyList<string> GetInboxNames() => _inboxContexts.Keys.Order(StringComparer.Ordinal).ToList();

    public void DeclareOutbox(string name, Type contextType) => Declare(_outboxContexts, "outbox", name, contextType);

    public void DeclareInbox(string name, Type contextType) => Declare(_inboxContexts, "inbox", name, contextType);

    /// <summary>The context of outbox <paramref name="outbox"/> from <paramref name="services"/>; inside a scope this is
    /// the instance the caller saves, so the row commits with the caller's change (R-107-3).</summary>
    public DbContext GetOutboxContext(IServiceProvider services, string outbox) =>
        Resolve(services, _outboxContexts, "Outbox", outbox);

    public DbContext GetInboxContext(IServiceProvider services, string inbox) =>
        Resolve(services, _inboxContexts, "Inbox", inbox);

    private static void Declare(Dictionary<string, Type> map, string kind, string name, Type contextType)
    {
        if (!map.TryAdd(name, contextType) && map[name] != contextType)
        {
            throw new InvalidOperationException(
                $"The {kind} '{name}' is already declared for {map[name].FullName}; it cannot also be declared for {contextType.FullName}.");
        }
    }

    private static DbContext Resolve(IServiceProvider services, Dictionary<string, Type> map, string kind, string name) =>
        map.TryGetValue(name, out var type)
            ? (DbContext)services.GetRequiredService(type)
            : throw new InvalidOperationException($"The {KindText(kind)} '{name}' is not declared; call {kind}<TContext>(\"{name}\") in AddMessaging.");

    private static string KindText(string kind) => string.Equals(kind, "Outbox", StringComparison.Ordinal) ? "outbox" : "inbox";

    private static void RequireDeclared(Dictionary<string, Type> map, string kind, string name, string by)
    {
        if (!map.ContainsKey(name))
        {
            throw new InvalidOperationException($"The {KindText(kind)} '{name}' is not declared; call {kind}<TContext>(\"{name}\") before {by}.");
        }
    }

    public EventDescriptor Register(Type eventType)
    {
        if (_byClrType.TryGetValue(eventType, out var known))
        {
            return known;
        }

        var descriptor = EventDescriptor.For(eventType);
        if (_byKey.TryGetValue((descriptor.Type, descriptor.Version), out var clash))
        {
            throw new InvalidOperationException(
                $"{eventType.FullName} and {clash.ClrType.FullName} both declare integration event '{descriptor.Type}' version {descriptor.Version}.");
        }

        _byKey[(descriptor.Type, descriptor.Version)] = descriptor;
        _byClrType[eventType] = descriptor;
        return descriptor;
    }

    public void AddPublisher(Type eventType, string outbox)
    {
        RequireDeclared(_outboxContexts, "Outbox", outbox, "Publishes");
        Register(eventType);
        if (!_outboxByClrType.TryAdd(eventType, outbox))
        {
            throw new InvalidOperationException($"{eventType.FullName} is already registered for publishing in outbox '{_outboxByClrType[eventType]}'.");
        }
    }

    public void AddSubscription(Subscription subscription)
    {
        RequireDeclared(_inboxContexts, "Inbox", subscription.InboxName, "Subscribe");
        var duplicate = _subscriptions.Any(s =>
            s.Event == subscription.Event
            && string.Equals(s.InboxName, subscription.InboxName, StringComparison.Ordinal)
            && string.Equals(s.HandlerName, subscription.HandlerName, StringComparison.Ordinal));
        if (duplicate)
        {
            throw new InvalidOperationException(
                $"Handler '{subscription.HandlerName}' is already subscribed to '{subscription.Event.Type}' in inbox '{subscription.InboxName}'.");
        }

        _subscriptions.Add(subscription);
    }

    public EventDescriptor? DescriptorFor(Type eventType) => _byClrType.GetValueOrDefault(eventType);

    public bool TryGetDescriptor(string type, int version, out EventDescriptor descriptor) =>
        _byKey.TryGetValue((type, version), out descriptor!);

    public string? OutboxFor(Type eventType) => _outboxByClrType.GetValueOrDefault(eventType);

    public IEnumerable<Subscription> SubscriptionsFor(EventDescriptor descriptor) =>
        _subscriptions.Where(s => s.Event == descriptor);
}
