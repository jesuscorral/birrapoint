using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Competitions;

/// <summary>Full competition detail wire shape (contracts/rest-api.md §Competitions).</summary>
public sealed record CompetitionDetailDto(
    Guid Id,
    string Name,
    string Venue,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Description,
    string? LogoUrl,
    int? EntryLimit,
    DateOnly? RegistrationStart,
    DateOnly? RegistrationEnd,
    CompetitionState State)
{
    public static CompetitionDetailDto FromEntity(Competition competition) => new(
        competition.Id,
        competition.Name,
        competition.Venue,
        competition.StartDate,
        competition.EndDate,
        competition.Description,
        competition.LogoUrl,
        competition.EntryLimit,
        competition.StartRegistration,
        competition.EndRegistration,
        competition.State);
}

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record GetCompetitionQuery(Guid Id) : IRequest<CompetitionDetailDto?>;

public sealed class GetCompetitionQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
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
