using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Competitions;

/// <summary>Wire shape for organizer-defined categories grouping allowed BJCP styles (wizard step
/// 3, contracts/rest-api.md §Competitions).</summary>
public sealed record CompetitionCategoriesDto(IReadOnlyList<CompetitionCategoryDto> Categories);

public sealed record CompetitionCategoryDto(Guid Id, string Name, int DisplayOrder, IReadOnlyList<string> StyleCodes);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record GetCompetitionCategoriesQuery(Guid CompetitionId) : IRequest<CompetitionCategoriesDto?>;

public sealed class GetCompetitionCategoriesQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<GetCompetitionCategoriesQuery, CompetitionCategoriesDto?>
{
    public async Task<CompetitionCategoriesDto?> Handle(GetCompetitionCategoriesQuery request, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (!competitionExists)
        {
            return null;
        }

        return await CompetitionCategoryProjection.LoadAsync(dbContext, request.CompetitionId, cancellationToken);
    }
}

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
