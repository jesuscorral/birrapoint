using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Judges;

public sealed record NotifiedJudgeDto(Guid Id, string Email);

public sealed record NotifyJudgesResult(IReadOnlyList<NotifiedJudgeDto> Queued);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record NotifyJudgesCommand(Guid CompetitionId) : IRequest<NotifyJudgesResult?>;

/// <summary>
/// FR-059/R-20: the explicit "Notify judges" action, decoupled from both provisioning paths
/// (RegisterJudges/FR-014, ConsolidateJudgeImport/FR-057) — enqueues SendInvitation for every
/// judge in the competition whose invitation is still Pending. A judge already Sent/Failed is
/// untouched (use the per-judge resend, ResendInvitation.cs, for those).
/// </summary>
public sealed class NotifyJudgesCommandHandler(
    AppDbContext dbContext, ICurrentUser currentUser, IDispatchJobQueue dispatchJobQueue)
    : IRequestHandler<NotifyJudgesCommand, NotifyJudgesResult?>
{
    public async Task<NotifyJudgesResult?> Handle(NotifyJudgesCommand request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (competition is null)
        {
            return null;
        }

        var pendingJudges = await dbContext.Judges
            .Where(j => j.CompetitionId == competition.Id)
            .Join(
                dbContext.Invitations.Where(i => i.Status == InvitationStatus.Pending),
                judge => judge.Id,
                invitation => invitation.JudgeId,
                (judge, invitation) => judge)
            .ToListAsync(cancellationToken);

        foreach (var judge in pendingJudges)
        {
            await dispatchJobQueue.EnqueueAsync(
                competition.Id, DispatchJobType.SendInvitation, new { JudgeId = judge.Id }, cancellationToken);
        }

        return new NotifyJudgesResult(pendingJudges.Select(j => new NotifiedJudgeDto(j.Id, j.Email)).ToList());
    }
}
