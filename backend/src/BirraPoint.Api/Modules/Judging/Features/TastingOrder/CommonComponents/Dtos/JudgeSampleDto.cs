namespace BirraPoint.Api.Modules.Judging.Features.TastingOrder.CommonComponents.Dtos;

/// <summary>
/// BR-01/FR-019 anonymity boundary (data-model.md §Anonymity boundary) — this file MUST NOT ever
/// gain a property for entrant data (BeerName, Participant.*, EntryCollaborator.*). Every
/// judge-facing query in this slice projects exclusively into these two shapes; a contract test
/// (OrderApiTests.GetTableSamples_payload_never_contains_entrant_fields) asserts the serialized
/// wire payload, not just this DTO's declared members.
///
/// data-model.md §Anonymity boundary — the canonical judge-facing sample projection.
/// AbvPercent is the beer's own physical attribute (already exposed to the organizer via
/// TableSampleDto) — not an entrant field, so it's safe alongside BlindCode/StyleCode.
/// </summary>
internal sealed record JudgeSampleDto(
    Guid BeerEntryId,
    string BlindCode,
    string StyleCode,
    string StyleName,
    decimal AbvPercent,
    int? SequenceOrder,
    string EvaluationStatus,
    string? EntryInstructions);
