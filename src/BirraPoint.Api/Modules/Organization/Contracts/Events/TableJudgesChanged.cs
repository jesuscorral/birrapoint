using MediatR;

namespace BirraPoint.Api.Modules.Organization.Contracts.Events;

/// <summary>In-process notification published by Judging before saving a table's new judge set, so
/// Organization can update the competition-wide NotValidForBos flag (FR-018) in the same unit of work.
/// <paramref name="ActiveJudgeIds"/> is the table's judge set after the change.
/// <paramref name="OwnedEntriesByJudgeId"/> carries, for every added and removed judge, the entries they own
/// as returned by <see cref="IConflictOfInterestChecker.GetEntriesOwnedByJudgesAsync"/>, so the publisher
/// and the handler share one lookup.</summary>
internal sealed record TableJudgesChanged(
    Guid CompetitionId,
    Guid TableId,
    IReadOnlyCollection<Guid> ActiveJudgeIds,
    IReadOnlyCollection<Guid> AddedJudgeIds,
    IReadOnlyCollection<Guid> RemovedJudgeIds,
    IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> OwnedEntriesByJudgeId) : INotification;
