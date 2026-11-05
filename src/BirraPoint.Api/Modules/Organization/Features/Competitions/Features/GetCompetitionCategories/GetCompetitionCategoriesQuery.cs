using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Features.Competitions.CommonComponents;
using BirraPoint.Api.Modules.Organization.Features.Competitions.CommonComponents.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Features.Competitions.Features.GetCompetitionCategories;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record GetCompetitionCategoriesQuery(Guid CompetitionId) : IRequest<CompetitionCategoriesDto?>;

internal sealed class GetCompetitionCategoriesQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
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
