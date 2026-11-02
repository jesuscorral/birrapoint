using BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents;

namespace BirraPoint.Api.Modules.Judging.Contracts.Dtos;

/// <summary>Apariencia. Color/Clarity/Foam are closed-list single choices (EvaluationDescriptorCatalog)
/// plus a free-text "Other" companion, matching the paper sheet's "Otros ____" line next to each.
/// Retention is a continuous 0–100 slider (Baja↔Alta, no discrete labels on the paper sheet).
/// Session 2026-09-21 (organizer follow-up): every rated attribute carries its own Inappropriate
/// flag — Texture/Notes are free-text annotations, not ratings, so they don't get one.</summary>
internal sealed record AppearanceDescriptorsDto(
    string? Color,
    string? ColorOther,
    bool ColorInappropriate,
    string? Clarity,
    bool ClarityInappropriate,
    string? Foam,
    string? FoamOther,
    bool FoamInappropriate,
    int? Retention,
    bool RetentionInappropriate,
    string? Texture,
    string? Notes);
