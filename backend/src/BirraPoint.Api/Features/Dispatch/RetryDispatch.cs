using System.Text.Json;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Dispatch;

/// <summary>Returns false when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record RetryDispatchCommand(Guid CompetitionId, IReadOnlyList<Guid> ParticipantIds) : IRequest<bool>;

public sealed class RetryDispatchCommandValidator : AbstractValidator<RetryDispatchCommand>
{
    public RetryDispatchCommandValidator()
    {
        RuleFor(c => c.ParticipantIds).NotEmpty();
    }
}

/// <summary>Re-queues failed result emails (FR-041) by resetting the SendResultEmail job back to a
/// fresh attempt. Only Failed jobs: a Completed one would resend the email, a Running one may still
/// be executing under another worker's lease (T129). After the commit the worker is woken
/// through IDispatchWakeUp (same as DispatchJobQueue), because the production safety-net poll
/// (Dispatch:SafetyNetPollInterval) can be hours long (T132).</summary>
public sealed class RetryDispatchCommandHandler(
    AppDbContext dbContext, ICurrentUser currentUser, IDispatchWakeUp wakeUp)
    : IRequestHandler<RetryDispatchCommand, bool>
{
    public async Task<bool> Handle(RetryDispatchCommand request, CancellationToken cancellationToken)
    {
        var owns = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);
        if (!owns)
        {
            return false;
        }

        var jobs = await dbContext.DispatchJobs
            .Where(j => j.CompetitionId == request.CompetitionId
                && j.Type == DispatchJobType.SendResultEmail
                && j.Status == DispatchJobStatus.Failed)
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

            job.Status = DispatchJobStatus.Pending;
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
