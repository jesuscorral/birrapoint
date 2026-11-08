using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Publication.Features.Dispatch.CommonComponents;
using BuildingBlocks.Identity;
using BuildingBlocks.Jobs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.RetryDispatch;

/// <summary>Returns false when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record RetryDispatchCommand(Guid CompetitionId, IReadOnlyList<Guid> ParticipantIds) : IRequest<bool>;

/// <summary>Re-queues failed result emails (FR-041) by resetting the SendResultEmail job back to a
/// fresh attempt. Only Failed jobs: a Completed one would resend the email, a Running one may still
/// be executing under another worker's lease. After the commit the worker is woken
/// through IJobWakeUp (same as DispatchJobQueue), because the production safety-net poll
/// (Dispatch:SafetyNetPollInterval) can be hours long.</summary>
internal sealed class RetryDispatchCommandHandler(
    AppDbContext dbContext, ICurrentUser currentUser, ICompetitionAccess competitionAccess, IJobWakeUp wakeUp)
    : IRequestHandler<RetryDispatchCommand, bool>
{
    public async Task<bool> Handle(RetryDispatchCommand request, CancellationToken cancellationToken)
    {
        if (await competitionAccess.GetOwnedAsync(request.CompetitionId, currentUser.Sub, cancellationToken) is null)
        {
            return false;
        }

        var jobs = await dbContext.DispatchJobs
            .OfDispatchType(DispatchJobType.SendResultEmail)
            .Where(j => j.GroupKey == request.CompetitionId
                && j.Status == JobStatus.Failed)
            .ToListAsync(cancellationToken);

        var participantIds = new HashSet<Guid>(request.ParticipantIds);
        var resetJobIds = new List<Guid>();

        foreach (var job in jobs)
        {
            var participantId = JsonSerializer.Deserialize<SendResultEmailPayload>(job.PayloadJson)!.ParticipantId;
            if (!participantIds.Contains(participantId))
            {
                continue;
            }

            job.Status = JobStatus.Pending;
            job.Attempts = 0;
            job.NextAttemptAt = null;
            job.LastError = null;
            job.LeaseOwner = null;
            job.LeaseExpiresAt = null;
            resetJobIds.Add(job.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Best-effort wake-up after commit (never fails the request). One signal is enough: the
        // worker drains the channel and sweeps everything pending.
        if (resetJobIds.Count > 0)
        {
            wakeUp.Signal();
        }

        return true;
    }
}
