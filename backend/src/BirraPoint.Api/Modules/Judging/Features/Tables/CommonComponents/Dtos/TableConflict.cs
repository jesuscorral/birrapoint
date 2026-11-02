namespace BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents.Dtos;

internal sealed record TableConflict(Guid JudgeId, IReadOnlyList<Guid> BeerEntryIds);
