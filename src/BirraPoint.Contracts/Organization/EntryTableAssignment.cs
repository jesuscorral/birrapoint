namespace BirraPoint.Contracts.Organization;

/// <summary>The tasting table an entry is assigned to.</summary>
public sealed record EntryTableAssignment(Guid TableId, string TableName);
