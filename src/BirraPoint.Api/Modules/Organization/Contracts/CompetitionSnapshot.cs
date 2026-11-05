namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>Minimal competition view for other modules: identity, name and lifecycle state.</summary>
internal sealed record CompetitionSnapshot(Guid Id, string Name, CompetitionState State);
