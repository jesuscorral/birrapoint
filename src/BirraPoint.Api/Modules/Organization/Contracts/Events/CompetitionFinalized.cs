using MediatR;

namespace BirraPoint.Api.Modules.Organization.Contracts.Events;

/// <summary>In-process notification published by Organization before the finalize transaction commits, so
/// subscribers (Publication) can stage their work in the same unit of work.</summary>
internal sealed record CompetitionFinalized(Guid CompetitionId) : INotification;
