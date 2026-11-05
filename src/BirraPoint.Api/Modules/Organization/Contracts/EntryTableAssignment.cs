namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>The tasting table an entry is assigned to.</summary>
internal sealed record EntryTableAssignment(Guid TableId, string TableName);
