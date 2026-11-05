namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>A judge that owns (as participant or collaborator) one or more of the checked entries.</summary>
internal sealed record JudgeEntryConflict(Guid JudgeId, IReadOnlyList<Guid> BeerEntryIds);
