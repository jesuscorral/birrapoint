using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Features.Competitions.CommonComponents.Dtos;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Competitions.CommonComponents;

/// <summary>Shared read projection for both GetCompetitionCategories and SetCompetitionCategories.</summary>
internal static class CompetitionCategoryProjection
{
    public static async Task<CompetitionCategoriesDto> LoadAsync(
        AppDbContext dbContext, Guid competitionId, CancellationToken cancellationToken)
    {
        var categories = await dbContext.CompetitionCategories
            .AsNoTracking()
            .Where(category => category.CompetitionId == competitionId)
            .OrderBy(category => category.DisplayOrder)
            .Select(category => new CompetitionCategoryDto(
                category.Id,
                category.Name,
                category.DisplayOrder,
                category.Styles.Select(style => style.StyleCode).OrderBy(code => code).ToList()))
            .ToListAsync(cancellationToken);

        return new CompetitionCategoriesDto(categories);
    }
}
