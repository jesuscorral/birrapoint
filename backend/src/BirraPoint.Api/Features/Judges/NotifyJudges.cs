using System.Text.Json;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Judges;

internal sealed record NotifiedJudgeDto(Guid Id, string Email);

internal sealed record NotifyJudgesResult(IReadOnlyList<NotifiedJudgeDto> Queued);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record NotifyJudgesCommand(Guid CompetitionId) : IRequest<NotifyJudgesResult?>;

/// <summary>
/// FR-059/R-20: the explicit "Notify judges" action, decoupled from both provisioning paths
/// (RegisterJudges/FR-014, ConsolidateJudgeImport/FR-057) — enqueues SendInvitation for every
/// judge in the competition whose invitation is still Pending. A judge already Sent/Failed is
/// untouched (use the per-judge resend, ResendInvitation.cs, for those). Judges that already have
/// a Pending/Running SendInvitation job are skipped (T147), so repeated notifies never duplicate emails.
/// </summary>
internal sealed class NotifyJudgesCommandHandler(
    AppDbContext dbContext, ICurrentUser currentUser, IDispatchWakeUp dispatchWakeUp)
    : IRequestHandler<NotifyJudgesCommand, NotifyJudgesResult?>
{
    public async Task<NotifyJudgesResult?> Handle(NotifyJudgesCommand request, CancellationToken cancellationToken)
    {
        var owned = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (!owned)
        {
            return null;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // T147/M1: the invitation only flips to Sent when the worker delivers, so two back-to-back
        // notifies would otherwise both see Pending and enqueue duplicate jobs (second temp password
        // + duplicate email). Row lock on the competition serializes concurrent notifies so the
        // in-flight check below is race-free; the loser sees the winner's committed jobs.
        _ = await dbContext.Competitions
            .FromSqlInterpolated($"SELECT * FROM \"Competitions\" WHERE \"Id\" = {request.CompetitionId} FOR UPDATE")
            .SingleAsync(cancellationToken);

        var pendingJudges = await dbContext.Judges
            .Where(j => j.CompetitionId == request.CompetitionId)
            .Join(
                dbContext.Invitations.Where(i => i.Status == InvitationStatus.Pending),
                judge => judge.Id,
                invitation => invitation.JudgeId,
                (judge, invitation) => judge)
            .ToListAsync(cancellationToken);

        var inFlightPayloads = await dbContext.DispatchJobs
            .Where(j => j.CompetitionId == request.CompetitionId
                && j.Type == DispatchJobType.SendInvitation
                && (j.Status == DispatchJobStatus.Pending || j.Status == DispatchJobStatus.Running))
            .Select(j => j.PayloadJson)
            .ToListAsync(cancellationToken);
        var alreadyQueued = NotifyJudgesRules.JudgeIdsWithInFlightInvitation(inFlightPayloads);

        var toQueue = pendingJudges.Where(j => !alreadyQueued.Contains(j.Id)).ToList();

        foreach (var judge in toQueue)
        {
            dbContext.DispatchJobs.Add(new DispatchJob
            {
                CompetitionId = request.CompetitionId,
                Type = DispatchJobType.SendInvitation,
                PayloadJson = JsonSerializer.Serialize(new SendInvitationPayload(judge.Id)),
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Wake the worker only after commit; the safety-net poll covers a missed signal.
        if (toQueue.Count > 0)
        {
            dispatchWakeUp.Signal();
        }

        return new NotifyJudgesResult(toQueue.Select(j => new NotifiedJudgeDto(j.Id, j.Email)).ToList());
    }
}
