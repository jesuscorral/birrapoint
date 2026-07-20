using BirraPoint.Api.Features.Tables;

namespace BirraPoint.Api.UnitTests.Tables;

/// <summary>
/// T045: pure BOS flag/unflag rules (FR-018) — flagging fires for any judge newly holding an
/// active table assignment; unflagging is eligible only before the judge's first submitted
/// evaluation, and is permanent afterward regardless of remaining assignments.
/// </summary>
public sealed class BosFlagRulesTests
{
    [Fact]
    public void EntriesOwnedByJudges_returns_the_entries_owned_by_a_newly_assigned_judge()
    {
        var judgeId = Guid.NewGuid();
        var entryId = Guid.NewGuid();

        var flagged = BosFlagRules.EntriesOwnedByJudges(
            [judgeId],
            new Dictionary<Guid, IReadOnlySet<Guid>> { [judgeId] = Set(entryId) });

        Assert.Equal([entryId], flagged);
    }

    [Fact]
    public void EntriesOwnedByJudges_unions_across_multiple_judges_and_dedups()
    {
        var judgeAId = Guid.NewGuid();
        var judgeBId = Guid.NewGuid();
        var sharedEntryId = Guid.NewGuid();
        var soloEntryId = Guid.NewGuid();

        var flagged = BosFlagRules.EntriesOwnedByJudges(
            [judgeAId, judgeBId],
            new Dictionary<Guid, IReadOnlySet<Guid>>
            {
                [judgeAId] = Set(sharedEntryId, soloEntryId),
                [judgeBId] = Set(sharedEntryId),
            });

        Assert.Equal(2, flagged.Count);
        Assert.Contains(sharedEntryId, flagged);
        Assert.Contains(soloEntryId, flagged);
    }

    [Fact]
    public void EntriesOwnedByJudges_returns_empty_for_a_judge_with_no_entries()
    {
        var judgeId = Guid.NewGuid();

        var flagged = BosFlagRules.EntriesOwnedByJudges(
            [judgeId], new Dictionary<Guid, IReadOnlySet<Guid>>());

        Assert.Empty(flagged);
    }

    [Fact]
    public void IsEligibleForUnflag_true_when_no_remaining_assignments_and_no_evaluations()
    {
        Assert.True(BosFlagRules.IsEligibleForUnflag(remainingActiveTableAssignments: 0, hasSubmittedAnyEvaluation: false));
    }

    [Fact]
    public void IsEligibleForUnflag_false_when_still_assigned_to_another_table()
    {
        Assert.False(BosFlagRules.IsEligibleForUnflag(remainingActiveTableAssignments: 1, hasSubmittedAnyEvaluation: false));
    }

    /// <summary>
    /// The permanence rule (FR-018): once a judge has submitted any evaluation, the flag can never
    /// be lifted for that competition again — even after they lose every table assignment.
    /// </summary>
    [Fact]
    public void IsEligibleForUnflag_false_once_judge_has_submitted_any_evaluation_even_with_zero_assignments()
    {
        Assert.False(BosFlagRules.IsEligibleForUnflag(remainingActiveTableAssignments: 0, hasSubmittedAnyEvaluation: true));
    }

    [Fact]
    public void IsEligibleForUnflag_false_when_both_still_assigned_and_has_evaluated()
    {
        Assert.False(BosFlagRules.IsEligibleForUnflag(remainingActiveTableAssignments: 2, hasSubmittedAnyEvaluation: true));
    }

    private static IReadOnlySet<Guid> Set(params Guid[] ids) => new HashSet<Guid>(ids);
}
