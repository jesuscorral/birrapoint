using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Features.Competitions.CommonComponents.Dtos;
using BuildingBlocks.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Features.Competitions.Features.GetCompetition;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record GetCompetitionQuery(Guid Id) : IRequest<CompetitionDetailDto?>;

internal sealed class GetCompetitionQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<GetCompetitionQuery, CompetitionDetailDto?>
{
    public async Task<CompetitionDetailDto?> Handle(GetCompetitionQuery request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        return competition is null ? null : CompetitionDetailDto.FromEntity(competition);
    }
}
