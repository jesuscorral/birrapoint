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
    public void ComputeMissingBlindCodes_empty_when_every_active_judge_evaluated_every_sample()
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
    public void ComputeMissingBlindCodes_returns_the_blind_code_of_a_sample_missing_one_judges_evaluation()
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
    public void ComputeMissingBlindCodes_dedups_a_sample_missing_more_than_one_judge()
    {
        var judgeA = Guid.NewGuid();
        var judgeB = Guid.NewGuid();
        var sample1 = Guid.NewGuid();
        var blindCodes = new Dictionary<Guid, string> { [sample1] = "B001" };

        var missing = CloseTableRules.ComputeMissingBlindCodes([judgeA, judgeB], [sample1], [], blindCodes);

        Assert.Equal(["B001"], missing);
    }

    [Fact]
    public void ComputeMissingBlindCodes_empty_when_there_are_no_active_judges()
    {
        var sample1 = Guid.NewGuid();
        var blindCodes = new Dictionary<Guid, string> { [sample1] = "B001" };

        var missing = CloseTableRules.ComputeMissingBlindCodes([], [sample1], [], blindCodes);

        Assert.Empty(missing);
    }

    [Fact]
    public void ComputeMissingBlindCodes_ignores_samples_with_no_active_judges_assigned_to_check()
    {
        // Sanity: a table with zero samples never blocks close on completeness.
        var judgeA = Guid.NewGuid();

        var missing = CloseTableRules.ComputeMissingBlindCodes(
            [judgeA], [], [], new Dictionary<Guid, string>());

        Assert.Empty(missing);
    }

    // ---- ComputeMean (FR-042) ---------------------------------------------------------------------

    [Fact]
    public void ComputeMean_averages_the_given_totals()
    {
        Assert.Equal(35m, CloseTableRules.ComputeMean([30, 40]));
    }

    [Fact]
    public void ComputeMean_averages_three_totals_to_a_fractional_result()
    {
        Assert.Equal(33.33m, CloseTableRules.ComputeMean([30, 40, 30]));
    }

    [Fact]
    public void ComputeMean_handles_a_single_total()
    {
        Assert.Equal(42m, CloseTableRules.ComputeMean([42]));
    }

    [Fact]
    public void ComputeMean_is_zero_for_no_totals()
    {
        Assert.Equal(0m, CloseTableRules.ComputeMean([]));
    }
}
