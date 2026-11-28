namespace BirraPoint.Contracts.Organization;

/// <summary>Minimal competition view for other modules: identity, name and lifecycle state.</summary>
public sealed record CompetitionSnapshot(Guid Id, string Name, CompetitionState State);
