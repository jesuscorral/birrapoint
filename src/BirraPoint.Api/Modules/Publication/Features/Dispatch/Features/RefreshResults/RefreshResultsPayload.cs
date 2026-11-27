namespace BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.RefreshResults;

/// <summary>Payload of a <c>RefreshResults</c> job: the entry whose evaluation was corrected.</summary>
internal sealed record RefreshResultsPayload(Guid BeerEntryId);
