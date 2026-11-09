using BuildingBlocks.Messaging;

namespace BirraPoint.Api.Modules.Organization.Contracts.IntegrationEvents;

/// <summary>Published by Organization in the transaction that corrects a judge's email (FR-082). Carries no
/// email: receivers recompute from current state (FR-085).</summary>
[IntegrationEvent("organization.judge-email-changed", 1)]
internal sealed record JudgeEmailChanged(Guid CompetitionId, Guid JudgeId);
