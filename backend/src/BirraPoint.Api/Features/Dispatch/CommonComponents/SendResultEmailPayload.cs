namespace BirraPoint.Api.Features.Dispatch.CommonComponents;

/// <summary>Payload shape enqueued by BundleZipHandler.</summary>
internal sealed record SendResultEmailPayload(Guid ParticipantId);
