using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Contracts.Events;
using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Organization.Errors;
using BirraPoint.Api.Realtime;
using BuildingBlocks.Audit;
using BuildingBlocks.Identity;
using BuildingBlocks.Jobs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Features.Competitions.Features.ChangeCompetitionState;

internal sealed record ChangeCompetitionStateResult(CompetitionState State);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record ChangeCompetitionStateCommand(Guid Id, CompetitionState Target)
    : IRequest<ChangeCompetitionStateResult?>;

internal sealed class ChangeCompetitionStateCommandHandler(
    AppDbContext dbContext, ICurrentUser currentUser, IAuditWriter auditWriter, IEventPublisher eventPublisher,
    ICompetitionFinalizationGuard finalizationGuard, IPublisher publisher, IJobWakeUp dispatchWakeUp)
    : IRequestHandler<ChangeCompetitionStateCommand, ChangeCompetitionStateResult?>
{
    public async Task<ChangeCompetitionStateResult?> Handle(
        ChangeCompetitionStateCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Row lock serializes concurrent transitions: racing requests must not all pass CanTransition on
        // the same stale state and each stage an audit row (or a GeneratePdfs job). Losers re-read the
        // committed state after the winner releases the lock and get a 409. The ownership filter stays in
        // the locked query so an unowned competition is still a plain 404.
        var sub = currentUser.Sub;
        var competition = await dbContext.Competitions
            .FromSqlInterpolated(
                $"SELECT * FROM \"Competitions\" WHERE \"Id\" = {request.Id} AND \"CreatedByUserId\" = {sub} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (competition is null)
        {
            return null;
        }

        if (!CompetitionStateMachine.CanTransition(competition.State, request.Target))
        {
            throw new OrganizationException(
                OrganizationError.InvalidStateTransition,
                $"Cannot transition from {competition.State} to {request.Target}.");
        }

        if (request.Target == CompetitionState.Finalized)
        {
            var openTableIds = await finalizationGuard.GetOpenTableIdsAsync(competition.Id, cancellationToken);

            if (openTableIds.Count > 0)
            {
                throw new OrganizationException(
                    OrganizationError.TablesStillOpen,
                    "Competition has tasting tables that are not closed.",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["openTableIds"] = openTableIds });
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

        // Subscribers (Publication stages the GeneratePdfs job) add their work to the same change
        // tracker, so state change, audit and job commit in one SaveChanges; a throwing subscriber
        // aborts before anything is saved.
        if (request.Target == CompetitionState.Finalized)
        {
            await publisher.Publish(new CompetitionFinalized(competition.Id), cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Everything below runs after the commit, so it must not observe the request token: an
        // aborted request would otherwise leave a Finalized competition with no worker wake-up.
        if (request.Target == CompetitionState.Finalized)
        {
            dispatchWakeUp.Signal();
        }

        // Emitted only after the transaction above commits (contracts/signalr-hub.md §Delivery
        // semantics) — never before, to avoid phantom events from a rolled-back change.
        await eventPublisher.PublishToOrganizersAsync(
            competition.Id,
            "CompetitionStateChanged",
            new { competitionId = competition.Id, state = competition.State },
            CancellationToken.None);

        return new ChangeCompetitionStateResult(competition.State);
    }
}
