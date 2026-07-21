namespace BirraPoint.Api.Features.TastingOrder;

/// <summary>
/// BR-01/FR-019 anonymity boundary (data-model.md §Anonymity boundary) — this file MUST NOT ever
/// gain a property for entrant data (BeerName, Participant.*, EntryCollaborator.*). Every
/// judge-facing query in this slice projects exclusively into these two shapes; a contract test
/// (OrderApiTests.GetTableSamples_payload_never_contains_entrant_fields) asserts the serialized
/// wire payload, not just this DTO's declared members.
/// </summary>
public sealed record JudgeTableSummaryDto(
    Guid TableId,
    string Name,
    string CompetitionState,
    string TableState,
    bool OrderFixed,
    string? OrderFixedBy);

/// <summary>data-model.md §Anonymity boundary — the canonical judge-facing sample projection.</summary>
public sealed record JudgeSampleDto(
    Guid BeerEntryId,
    string BlindCode,
    string StyleCode,
    string StyleName,
    int? SequenceOrder,
    string EvaluationStatus);
