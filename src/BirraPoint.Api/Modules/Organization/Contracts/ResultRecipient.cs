namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>A participant that receives their results by e-mail (FR-041).</summary>
internal sealed record ResultRecipient(Guid ParticipantId, string Email);
