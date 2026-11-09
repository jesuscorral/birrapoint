using BirraPoint.Api.Domain;
using BirraPoint.Api.Modules.Judging.Contracts.IntegrationEvents;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging;

namespace BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.RefreshResults;

/// <summary>
/// FR-083: a correction on a finalized competition stages one <see cref="DispatchJobType.RefreshResults"/> job in
/// the delivery's own transaction (the inbox row makes a redelivery stage nothing more) and wakes the worker once
/// it commits.
/// </summary>
internal sealed class StageRefreshOnEvaluationCorrected(IJobQueue dispatchJobQueue, IJobWakeUp dispatchWakeUp)
    : IIntegrationEventHandler<EvaluationCorrected>
{
    public Task HandleAsync(
        EvaluationCorrected integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        dispatchJobQueue.Stage(
            integrationEvent.CompetitionId, DispatchJobType.RefreshResults, new RefreshResultsPayload(integrationEvent.BeerEntryId));
        context.OnCommitted(_ =>
        {
            dispatchWakeUp.Signal();
            return Task.CompletedTask;
        });
        return Task.CompletedTask;
    }
}
