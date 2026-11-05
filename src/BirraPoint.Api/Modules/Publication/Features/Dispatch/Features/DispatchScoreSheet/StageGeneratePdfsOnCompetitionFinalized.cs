using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Modules.Organization.Contracts.Events;
using MediatR;

namespace BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.DispatchScoreSheet;

/// <summary>
/// Starts the results pipeline when a competition is finalized (FR-036): stages the
/// <see cref="DispatchJobType.GeneratePdfs"/> job on the publisher's unit of work, so it commits (or
/// rolls back) together with the state change. The publisher signals the worker after the commit.
/// </summary>
internal sealed class StageGeneratePdfsOnCompetitionFinalized(IDispatchJobQueue dispatchJobQueue)
    : INotificationHandler<CompetitionFinalized>
{
    public Task Handle(CompetitionFinalized notification, CancellationToken cancellationToken)
    {
        dispatchJobQueue.Stage(notification.CompetitionId, DispatchJobType.GeneratePdfs, new { });
        return Task.CompletedTask;
    }
}
