namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>An entry as results delivery needs it: who entered it and how its score sheet is named.</summary>
internal sealed record ResultEntry(Guid EntryId, Guid ParticipantId, string BlindCode, string StyleCode);
