namespace BirraPoint.Api.Modules.Organization.Features.Competitions.CommonComponents.Dtos;

internal sealed record CompetitionCategoryDto(Guid Id, string Name, int DisplayOrder, IReadOnlyList<string> StyleCodes);
