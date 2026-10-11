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
    public void TwoJudgesExactly7ApartAreNotInvolved()
    {
        var judgeA = Guid.NewGuid();
        var judgeB = Guid.NewGuid();
        var totals = new Dictionary<Guid, int> { [judgeA] = 40, [judgeB] = 33 };

        var involved = DiscrepancyRules.ComputeInvolvedJudgeIds(totals);

        Assert.Empty(involved);
    }

    [Fact]
    public void TwoJudges8ApartAreBothInvolved()
    {
        var judgeA = Guid.NewGuid();
        var judgeB = Guid.NewGuid();
        var totals = new Dictionary<Guid, int> { [judgeA] = 40, [judgeB] = 32 };

        var involved = DiscrepancyRules.ComputeInvolvedJudgeIds(totals);

        Assert.Equal(new HashSet<Guid> { judgeA, judgeB }, involved);
    }

    [Fact]
    public void ThreeJudgesOnlyTheOutliersMoreThan7ApartAreInvolved()
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
    public void SingleJudgeHasNoOneInvolved()
    {
        var totals = new Dictionary<Guid, int> { [Guid.NewGuid()] = 40 };

        var involved = DiscrepancyRules.ComputeInvolvedJudgeIds(totals);

        Assert.Empty(involved);
    }

    [Fact]
    public void AllJudgesWithin7OfEachOtherResolveToAnEmptySet()
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
