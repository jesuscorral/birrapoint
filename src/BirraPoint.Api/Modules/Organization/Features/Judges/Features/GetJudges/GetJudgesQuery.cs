using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Features.Judges.CommonComponents.Dtos;
using BuildingBlocks.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Features.Judges.Features.GetJudges;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record GetJudgesQuery(Guid CompetitionId) : IRequest<IReadOnlyList<JudgeProfileDto>?>;

internal sealed class GetJudgesQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
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
                    judge.Id, judge.Email, judge.DisplayName,
                    judge.BjcpRank, judge.BjcpId, judge.PreferredCategory, judge.Preferences,
                    invitation.Status, invitation.Attempts, invitation.LastError, invitation.SentAt))
            .ToListAsync(cancellationToken);
    }
}
