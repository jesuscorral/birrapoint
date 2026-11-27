namespace BuildingBlocks.Messaging;

/// <summary>An integration event as it travels between the outbox and a transport.</summary>
/// <param name="Id">Event id (v7 Guid); the inbox de-duplication key together with the handler name.</param>
/// <param name="Type">Stable type name from <see cref="IntegrationEventAttribute"/>.</param>
/// <param name="Version">Payload version from <see cref="IntegrationEventAttribute"/>.</param>
/// <param name="OccurredAt">When the publishing transaction staged the event.</param>
/// <param name="PayloadJson">Serialized event, camelCase.</param>
public sealed record IntegrationEventEnvelope(Guid Id, string Type, int Version, DateTimeOffset OccurredAt, string PayloadJson);
