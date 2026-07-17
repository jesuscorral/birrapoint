using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Common.Jobs;

/// <summary>`{ jobType, status, detail? }` per contracts/signalr-hub.md's `DispatchProgress` event.</summary>
public sealed record DispatchProgressPayload(DispatchJobType JobType, DispatchJobStatus Status, string? Detail);
