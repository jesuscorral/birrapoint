namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>A competition judge as other modules see it: identity, contact e-mail (organizer views only) and
/// display data. One person judging several competitions has one summary per competition.</summary>
internal sealed record JudgeSummary(Guid Id, Guid CompetitionId, string Email, string DisplayName, string? BjcpRank);
