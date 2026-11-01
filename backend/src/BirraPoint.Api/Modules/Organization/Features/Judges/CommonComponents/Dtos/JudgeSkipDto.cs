namespace BirraPoint.Api.Modules.Organization.Features.Judges.CommonComponents.Dtos;

/// <summary>Reason is one of "duplicate-in-list" | "already-registered" (contracts/rest-api.md §Judges).</summary>
internal sealed record JudgeSkipDto(string Email, string Reason);
