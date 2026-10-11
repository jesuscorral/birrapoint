using BirraPoint.Api.Features.Evaluations;

namespace BirraPoint.Api.UnitTests.Evaluations;

/// <summary>
/// T062: pure CloseTable rules (FR-033/FR-042) — the completeness precondition (every active
/// judge must have evaluated every sample) and the consolidated-mean averaging math. Exercises
/// <see cref="CloseTableRules"/> directly (same "pure rule class beside the DB-touching handler"
/// pattern already used by SubmitEvaluationRules/TastingOrderRules) rather than the full MediatR
/// handler, which needs a real transaction against Postgres (Testcontainers) — CloseTableApiTests
/// (T063) covers the end-to-end precondition chain, the discrepancy-open gate (inherently a plain
/// EF filter with no meaningful pure core), and the double-close/post-close-sync scenarios.
/// </summary>
public sealed class CloseTableTests
{
    // ---- ComputeMissingBlindCodes ----------------------------------------------------------------

    [Fact]
    public void ComputeMissingBlindCodesEmptyWhenEveryActiveJudgeEvaluatedEverySample()
    {
        var judgeA = Guid.NewGuid();
        var judgeB = Guid.NewGuid();
        var sample1 = Guid.NewGuid();
        var sample2 = Guid.NewGuid();
        var blindCodes = new Dictionary<Guid, string> { [sample1] = "B001", [sample2] = "B002" };
        var submitted = new[] { (judgeA, sample1), (judgeA, sample2), (judgeB, sample1), (judgeB, sample2) };

        var missing = CloseTableRules.ComputeMissingBlindCodes([judgeA, judgeB], [sample1, sample2], submitted, blindCodes);

        Assert.Empty(missing);
    }

    [Fact]
    public void ComputeMissingBlindCodesReturnsTheBlindCodeOfASampleMissingOneJudgesEvaluation()
    {
        var judgeA = Guid.NewGuid();
        var judgeB = Guid.NewGuid();
        var sample1 = Guid.NewGuid();
        var sample2 = Guid.NewGuid();
        var blindCodes = new Dictionary<Guid, string> { [sample1] = "B001", [sample2] = "B002" };
        // judgeB never evaluated sample2.
        var submitted = new[] { (judgeA, sample1), (judgeA, sample2), (judgeB, sample1) };

        var missing = CloseTableRules.ComputeMissingBlindCodes([judgeA, judgeB], [sample1, sample2], submitted, blindCodes);

        Assert.Equal(["B002"], missing);
    }

    [Fact]
    public void ComputeMissingBlindCodesDedupsASampleMissingMoreThanOneJudge()
    {
        var judgeA = Guid.NewGuid();
        var judgeB = Guid.NewGuid();
        var sample1 = Guid.NewGuid();
        var blindCodes = new Dictionary<Guid, string> { [sample1] = "B001" };

        var missing = CloseTableRules.ComputeMissingBlindCodes([judgeA, judgeB], [sample1], [], blindCodes);

        Assert.Equal(["B001"], missing);
    }

    [Fact]
    public void ComputeMissingBlindCodesEmptyWhenThereAreNoActiveJudges()
    {
        var sample1 = Guid.NewGuid();
        var blindCodes = new Dictionary<Guid, string> { [sample1] = "B001" };

        var missing = CloseTableRules.ComputeMissingBlindCodes([], [sample1], [], blindCodes);

        Assert.Empty(missing);
    }

    [Fact]
    public void ComputeMissingBlindCodesIgnoresSamplesWithNoActiveJudgesAssignedToCheck()
    {
        // Sanity: a table with zero samples never blocks close on completeness.
        var judgeA = Guid.NewGuid();

        var missing = CloseTableRules.ComputeMissingBlindCodes(
            [judgeA], [], [], new Dictionary<Guid, string>());

        Assert.Empty(missing);
    }

    // ---- ComputeMean (FR-042) ---------------------------------------------------------------------

    [Fact]
    public void ComputeMeanAveragesTheGivenTotals()
    {
        Assert.Equal(35m, CloseTableRules.ComputeMean([30, 40]));
    }

    [Fact]
    public void ComputeMeanAveragesThreeTotalsToAFractionalResult()
    {
        Assert.Equal(33.33m, CloseTableRules.ComputeMean([30, 40, 30]));
    }

    [Fact]
    public void ComputeMeanHandlesASingleTotal()
    {
        Assert.Equal(42m, CloseTableRules.ComputeMean([42]));
    }

    [Fact]
    public void ComputeMeanIsZeroForNoTotals()
    {
        Assert.Equal(0m, CloseTableRules.ComputeMean([]));
    }
}
