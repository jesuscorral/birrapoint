namespace BirraPoint.Api.Features.Evaluations.CommonComponents.Dtos;

/// <summary>contracts/rest-api.md §Judge workspace `{ alertId, blindCode, totals: [...] }`.</summary>
internal sealed record DiscrepancyView(Guid AlertId, string BlindCode, IReadOnlyList<DiscrepancyTotalDto> Totals);
