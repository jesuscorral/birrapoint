using System.Reflection;
using System.Text.Json;

namespace BuildingBlocks.Messaging;

/// <summary>An event class with its stable type name and version, read from <see cref="IntegrationEventAttribute"/>.</summary>
internal sealed record EventDescriptor(Type ClrType, string Type, int Version)
{
    public static EventDescriptor For<TEvent>() => For(typeof(TEvent));

    public static EventDescriptor For(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        var attribute = eventType.GetCustomAttribute<IntegrationEventAttribute>(inherit: false)
            ?? throw new InvalidOperationException(
                $"{eventType.FullName} must be annotated with [{nameof(IntegrationEventAttribute)}] to be an integration event.");
        return new EventDescriptor(eventType, attribute.Type, attribute.Version);
    }
}

internal static class EventSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IntegrationEventEnvelope ToEnvelope(EventDescriptor descriptor, object evt, Guid id, DateTimeOffset occurredAt) =>
        new(id, descriptor.Type, descriptor.Version, occurredAt, JsonSerializer.Serialize(evt, descriptor.ClrType, Options));

    public static object Deserialize(EventDescriptor descriptor, string payloadJson) =>
        JsonSerializer.Deserialize(payloadJson, descriptor.ClrType, Options)
        ?? throw new InvalidOperationException($"The payload of '{descriptor.Type}' is JSON null.");
}
