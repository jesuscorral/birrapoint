using BirraPoint.Api.Domain;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Contracts.IntegrationEvents;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging;

namespace BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.DispatchScoreSheet;

/// <summary>
/// Starts the results pipeline when a competition is finalized (FR-036): stages the
/// <see cref="DispatchJobType.GeneratePdfs"/> job in the delivery's own transaction, together with the inbox
/// row, so a redelivery stages nothing more (FR-079). The job worker is woken once that transaction commits.
/// </summary>
internal sealed class StageGeneratePdfsOnCompetitionFinalized(
    ICompetitionAccess competitionAccess, IJobQueue dispatchJobQueue, IJobWakeUp dispatchWakeUp)
    : IIntegrationEventHandler<CompetitionFinalized>
{
    public async Task HandleAsync(
        CompetitionFinalized integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        // A competition deleted since the event was stored has nothing left to publish.
        if (await competitionAccess.GetAsync(integrationEvent.CompetitionId, cancellationToken) is null)
        {
            return;
        }

        dispatchJobQueue.Stage(integrationEvent.CompetitionId, DispatchJobType.GeneratePdfs, new { });
        context.OnCommitted(_ =>
        {
            dispatchWakeUp.Signal();
            return Task.CompletedTask;
        });
    }
}
