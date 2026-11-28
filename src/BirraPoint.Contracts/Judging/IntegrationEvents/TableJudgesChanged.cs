using BuildingBlocks.Messaging;

namespace BirraPoint.Contracts.Judging.IntegrationEvents;

/// <summary>Published by Judging in the transaction that changes a table's judge set, so Organization
/// recomputes the Best of Show flags (FR-018). Identifiers only (FR-085).</summary>
[IntegrationEvent("judging.table-judges-changed", 1)]
internal sealed record TableJudgesChanged(
    Guid CompetitionId,
    Guid TableId,
    IReadOnlyCollection<Guid> AddedJudgeIds,
    IReadOnlyCollection<Guid> RemovedJudgeIds);
