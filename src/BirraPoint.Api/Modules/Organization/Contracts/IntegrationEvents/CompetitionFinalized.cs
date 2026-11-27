using BuildingBlocks.Messaging;

namespace BirraPoint.Api.Modules.Organization.Contracts.IntegrationEvents;

/// <summary>Published by Organization in the transaction that finalizes a competition (FR-036, FR-076).
/// Identifiers only (FR-085); receivers read the current state themselves.</summary>
[IntegrationEvent("organization.competition-finalized", 1)]
internal sealed record CompetitionFinalized(Guid CompetitionId);
