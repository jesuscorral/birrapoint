using MediatR;

namespace BirraPoint.Api.Modules.Organization.Contracts.Events;

/// <summary>In-process notification published by Judging before saving a table's new judge set, so
/// Organization can update the competition-wide NotValidForBos flag (FR-018) in the same unit of work.
/// <paramref name="ActiveJudgeIds"/> is the table's judge set after the change.</summary>
internal sealed record TableJudgesChanged(
    Guid CompetitionId,
    Guid TableId,
    IReadOnlyCollection<Guid> ActiveJudgeIds,
    IReadOnlyCollection<Guid> AddedJudgeIds,
    IReadOnlyCollection<Guid> RemovedJudgeIds) : INotification;
