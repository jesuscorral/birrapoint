using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Competitions;

/// <summary>Summary wire shape for the organizer's own competitions list (contracts/rest-api.md §Competitions).</summary>
public sealed record CompetitionSummaryDto(
    Guid Id, string Name, string Venue, DateOnly StartDate, DateOnly EndDate, CompetitionState State);

public sealed record ListCompetitionsQuery : IRequest<IReadOnlyList<CompetitionSummaryDto>>;

public sealed class ListCompetitionsQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<ListCompetitionsQuery, IReadOnlyList<CompetitionSummaryDto>>
{
    public async Task<IReadOnlyList<CompetitionSummaryDto>> Handle(
        ListCompetitionsQuery request, CancellationToken cancellationToken) =>
        await dbContext.Competitions
            .AsNoTracking()
            .Where(c => c.CreatedByUserId == currentUser.Sub)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CompetitionSummaryDto(c.Id, c.Name, c.Venue, c.StartDate, c.EndDate, c.State))
            .ToListAsync(cancellationToken);
}
