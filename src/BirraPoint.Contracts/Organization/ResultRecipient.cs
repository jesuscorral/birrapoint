namespace BirraPoint.Contracts.Organization;

/// <summary>A participant that receives their results by e-mail (FR-041).</summary>
public sealed record ResultRecipient(Guid ParticipantId, string Email);
