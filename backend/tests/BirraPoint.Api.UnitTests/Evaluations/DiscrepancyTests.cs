using BirraPoint.Api.Features.Evaluations;

namespace BirraPoint.Api.UnitTests.Evaluations;

/// <summary>
/// T079: pure FR-031 discrepancy rule — pairwise >7-point spread detection across 2..N judges
/// (same "pure rule class beside the DB-touching handler" pattern as SubmitEvaluationRules). The
/// full reconciliation (Evaluation.Status transitions, DiscrepancyAlert open/resolve) needs a real
/// DbContext — covered by DiscrepancyApiTests.cs (T080).
/// </summary>
public sealed class DiscrepancyTests
{
    [Fact]
    public void Two_judges_exactly_7_apart_are_not_involved()
    {
        var judgeA = Guid.NewGuid();
        var judgeB = Guid.NewGuid();
        var totals = new Dictionary<Guid, int> { [judgeA] = 40, [judgeB] = 33 };

        var involved = DiscrepancyRules.ComputeInvolvedJudgeIds(totals);

        Assert.Empty(involved);
    }

    [Fact]
    public void Two_judges_8_apart_are_both_involved()
    {
        var judgeA = Guid.NewGuid();
        var judgeB = Guid.NewGuid();
        var totals = new Dictionary<Guid, int> { [judgeA] = 40, [judgeB] = 32 };

        var involved = DiscrepancyRules.ComputeInvolvedJudgeIds(totals);

        Assert.Equal(new HashSet<Guid> { judgeA, judgeB }, involved);
    }

    [Fact]
    public void Three_judges_only_the_outliers_more_than_7_apart_are_involved()
    {
        var low = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var high = Guid.NewGuid();
        // 40 vs 45 -> 5 apart (not involved together); 40 vs 50 -> 10 apart (involved);
        // 45 vs 50 -> 5 apart (not involved together). Only `low` and `high` end up involved.
        var totals = new Dictionary<Guid, int> { [low] = 40, [mid] = 45, [high] = 50 };

        var involved = DiscrepancyRules.ComputeInvolvedJudgeIds(totals);

        Assert.Equal(new HashSet<Guid> { low, high }, involved);
    }

    [Fact]
    public void Single_judge_has_no_one_involved()
    {
        var totals = new Dictionary<Guid, int> { [Guid.NewGuid()] = 40 };

        var involved = DiscrepancyRules.ComputeInvolvedJudgeIds(totals);

        Assert.Empty(involved);
    }

    [Fact]
    public void All_judges_within_7_of_each_other_resolve_to_an_empty_set()
    {
        var totals = new Dictionary<Guid, int>
        {
            [Guid.NewGuid()] = 40,
            [Guid.NewGuid()] = 41,
            [Guid.NewGuid()] = 44,
            [Guid.NewGuid()] = 46,
        };

        var involved = DiscrepancyRules.ComputeInvolvedJudgeIds(totals);

        Assert.Empty(involved);
    }
}
