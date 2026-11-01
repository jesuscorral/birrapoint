using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Features.Judges.CommonComponents.Dtos;

/// <summary>BjcpRank/BjcpId/PreferredCategory/Preferences (US14/FR-057) are null for judges
/// created via the plain email-list flow (RegisterJudges), which doesn't collect them.</summary>
internal sealed record JudgeProfileDto(
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
