using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Features.Judges;

public sealed record RegisterJudgesRequest(IReadOnlyList<string> Emails);

public sealed record CreatedJudgeDto(Guid Id, string Email);

/// <summary>Reason is one of "duplicate-in-list" | "already-registered" (contracts/rest-api.md §Judges).</summary>
public sealed record JudgeSkipDto(string Email, string Reason);

public sealed record RegisterJudgesResult(IReadOnlyList<CreatedJudgeDto> Created, IReadOnlyList<JudgeSkipDto> Skipped);

/// <summary>BjcpRank/BjcpId/PreferredCategory/Preferences (US14/FR-057) are null for judges
/// created via the plain email-list flow above, which doesn't collect them.</summary>
public sealed record JudgeProfileDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? BjcpRank,
    string? BjcpId,
    string? PreferredCategory,
    string? Preferences,
    InvitationStatus InvitationStatus,
    int Attempts,
    string? LastError,
    DateTimeOffset? SentAt);

public sealed record UpdateJudgeEmailRequest(string Email);

public sealed record ResendInvitationResult(InvitationStatus Status);
