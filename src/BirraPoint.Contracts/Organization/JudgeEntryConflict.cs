namespace BirraPoint.Contracts.Organization;

/// <summary>A judge that owns (as participant or collaborator) one or more of the checked entries.</summary>
public sealed record JudgeEntryConflict(Guid JudgeId, IReadOnlyList<Guid> BeerEntryIds);
