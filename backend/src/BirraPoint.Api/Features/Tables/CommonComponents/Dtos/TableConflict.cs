namespace BirraPoint.Api.Features.Tables.CommonComponents.Dtos;

internal sealed record TableConflict(Guid JudgeId, IReadOnlyList<Guid> BeerEntryIds);
