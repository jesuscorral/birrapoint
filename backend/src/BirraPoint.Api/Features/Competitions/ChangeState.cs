using BirraPoint.Api.Common.Audit;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Realtime;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Competitions;

public sealed record ChangeCompetitionStateResult(CompetitionState State);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record ChangeCompetitionStateCommand(Guid Id, CompetitionState Target)
    : IRequest<ChangeCompetitionStateResult?>;

public sealed class ChangeCompetitionStateCommandHandler(
    AppDbContext dbContext, ICurrentUser currentUser, IAuditWriter auditWriter, IEventPublisher eventPublisher,
    IDispatchJobQueue dispatchJobQueue)
    : IRequestHandler<ChangeCompetitionStateCommand, ChangeCompetitionStateResult?>
{
    public async Task<ChangeCompetitionStateResult?> Handle(
        ChangeCompetitionStateCommand request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (competition is null)
        {
            return null;
        }

        if (!CompetitionStateMachine.CanTransition(competition.State, request.Target))
        {
            throw new DomainException(
                DomainErrorType.InvalidStateTransition,
                $"Cannot transition from {competition.State} to {request.Target}.");
        }

        if (request.Target == CompetitionState.Finalized)
        {
            var openTableIds = await dbContext.TastingTables
                .Where(t => t.CompetitionId == competition.Id && t.State != TableState.Closed)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            if (openTableIds.Count > 0)
            {
                throw new DomainException(
                    DomainErrorType.TablesStillOpen,
                    "Competition has tasting tables that are not closed.",
                    new Dictionary<string, object?> { ["openTableIds"] = openTableIds });
            }
        }

        var previousState = competition.State;
        competition.State = request.Target;

        // Audit stages via the change tracker (does not call SaveChanges) — must run before our
        // own SaveChangesAsync so both commit in the same transaction.
        auditWriter.Record(
            "CompetitionStateChanged",
            nameof(Competition),
            competition.Id.ToString(),
            before: new { State = previousState },
            after: new { State = competition.State });

        await dbContext.SaveChangesAsync(cancellationToken);

        // Emitted only after the transaction above commits (contracts/signalr-hub.md §Delivery
        // semantics) — never before, to avoid phantom events from a rolled-back change.
        await eventPublisher.PublishToOrganizersAsync(
            competition.Id,
            "CompetitionStateChanged",
            new { competitionId = competition.Id, state = competition.State });

        if (request.Target == CompetitionState.Finalized)
        {
            // FR-036's actual trigger: background PDF/ZIP/email dispatch starts once the
            // competition is Finalized, same after-commit timing as the event above.
            await dispatchJobQueue.EnqueueAsync(competition.Id, DispatchJobType.GeneratePdfs, new { }, cancellationToken);
        }

        return new ChangeCompetitionStateResult(competition.State);
    }
}
