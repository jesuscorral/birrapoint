using BuildingBlocks.Messaging;

namespace BirraPoint.Api.Modules.Judging.Contracts.IntegrationEvents;

/// <summary>Published by Judging when an organizer corrects an evaluation of a finalized competition, so
/// Publication refreshes the results already generated (FR-083). Identifiers only (FR-085).</summary>
[IntegrationEvent("judging.evaluation-corrected", 1)]
internal sealed record EvaluationCorrected(Guid CompetitionId, Guid BeerEntryId, Guid EvaluationId);
