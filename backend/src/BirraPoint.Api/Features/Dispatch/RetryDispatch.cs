using System.Text.Json;
using BirraPoint.Api.Common.Auth;
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
/// fresh attempt — DispatchWorker's existing 30s safety-net poll picks up Pending jobs regardless
/// of NextAttemptAt being cleared, no separate wake-up needed.</summary>
public sealed class RetryDispatchCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
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
            .Where(j => j.CompetitionId == request.CompetitionId && j.Type == DispatchJobType.SendResultEmail)
            .ToListAsync(cancellationToken);

        var participantIds = new HashSet<Guid>(request.ParticipantIds);

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
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
