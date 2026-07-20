using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Judges;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record GetJudgesQuery(Guid CompetitionId) : IRequest<IReadOnlyList<JudgeProfileDto>?>;

public sealed class GetJudgesQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<GetJudgesQuery, IReadOnlyList<JudgeProfileDto>?>
{
    public async Task<IReadOnlyList<JudgeProfileDto>?> Handle(GetJudgesQuery request, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (!competitionExists)
        {
            return null;
        }

        return await dbContext.Judges
            .Where(j => j.CompetitionId == request.CompetitionId)
            .Join(
                dbContext.Invitations,
                judge => judge.Id,
                invitation => invitation.JudgeId,
                (judge, invitation) => new JudgeProfileDto(
                    judge.Id, judge.Email, judge.DisplayName, invitation.Status, invitation.Attempts, invitation.LastError, invitation.SentAt))
            .ToListAsync(cancellationToken);
    }
}
