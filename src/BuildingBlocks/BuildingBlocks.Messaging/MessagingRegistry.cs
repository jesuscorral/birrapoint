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
    private readonly List<Subscription> _subscriptions = [];

    public IReadOnlyList<Subscription> Subscriptions => _subscriptions;

    /// <summary>Outboxes the dispatcher sweeps and the purger cleans.</summary>
    public IReadOnlyList<string> GetOutboxNames() => _outboxByClrType.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    public IReadOnlyList<string> GetInboxNames() => _subscriptions.Select(s => s.InboxName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

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
        Register(eventType);
        if (!_outboxByClrType.TryAdd(eventType, outbox))
        {
            throw new InvalidOperationException($"{eventType.FullName} is already registered for publishing in outbox '{_outboxByClrType[eventType]}'.");
        }
    }

    public void AddSubscription(Subscription subscription)
    {
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
