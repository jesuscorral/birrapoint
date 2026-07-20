using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Judges;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record ResendInvitationCommand(Guid CompetitionId, Guid JudgeId) : IRequest<ResendInvitationResult?>;

public sealed class ResendInvitationCommandHandler(
    AppDbContext dbContext, ICurrentUser currentUser, IDispatchJobQueue dispatchJobQueue)
    : IRequestHandler<ResendInvitationCommand, ResendInvitationResult?>
{
    public async Task<ResendInvitationResult?> Handle(ResendInvitationCommand request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (competition is null)
        {
            return null;
        }

        var judge = await dbContext.Judges
            .FirstOrDefaultAsync(j => j.Id == request.JudgeId && j.CompetitionId == competition.Id, cancellationToken);

        if (judge is null)
        {
            return null;
        }

        var invitation = await dbContext.Invitations.FirstAsync(i => i.JudgeId == judge.Id, cancellationToken);

        // Attempts is a running total across every send/resend cycle (mirrors DispatchJob.Attempts'
        // own convention) — only the delivery status resets here.
        invitation.Status = InvitationStatus.Pending;

        await dbContext.SaveChangesAsync(cancellationToken);

        await dispatchJobQueue.EnqueueAsync(
            competition.Id, DispatchJobType.SendInvitation, new { JudgeId = judge.Id }, cancellationToken);

        return new ResendInvitationResult(invitation.Status);
    }
}
