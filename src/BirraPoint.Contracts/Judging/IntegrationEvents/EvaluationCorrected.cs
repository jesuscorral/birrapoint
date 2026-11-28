using BuildingBlocks.Messaging;

namespace BirraPoint.Contracts.Judging.IntegrationEvents;

/// <summary>Published by Judging when an organizer corrects an evaluation of a finalized competition, so
/// Publication refreshes the results already generated (FR-083). Identifiers only (FR-085).</summary>
[IntegrationEvent("judging.evaluation-corrected", 1)]
public sealed record EvaluationCorrected(Guid CompetitionId, Guid BeerEntryId, Guid EvaluationId);
