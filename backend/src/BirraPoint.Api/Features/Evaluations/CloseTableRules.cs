namespace BirraPoint.Api.Features.Evaluations;

/// <summary>
/// Pure FR-033/FR-042 rules for closing a table — no EF/DB dependency, so these are unit-testable
/// directly (same "pure rule class beside the DB-touching handler" pattern already used by
/// SubmitEvaluationRules/TastingOrderRules/BosFlagRules/CoiDetector). The discrepancy-open
/// precondition (FR-032) doesn't factor into a meaningful pure function — it's a single EF filter
/// on DiscrepancyAlert.Status — so it stays inline in CloseTableCommandHandler.
/// </summary>
public static class CloseTableRules
{
    /// <summary>
    /// FR-033 completeness precondition: every judge in <paramref name="activeJudgeIds"/> must have
    /// a submitted (judge, sample) pair in <paramref name="submittedPairs"/> for every sample in
    /// <paramref name="sampleBeerEntryIds"/>. Returns the blind codes (via
    /// <paramref name="blindCodeByEntryId"/>), in <paramref name="sampleBeerEntryIds"/> order and
    /// deduplicated per sample, of the samples missing at least one active judge's evaluation —
    /// empty when the table is complete.
    /// </summary>
    public static IReadOnlyList<string> ComputeMissingBlindCodes(
        IReadOnlyCollection<Guid> activeJudgeIds,
        IReadOnlyCollection<Guid> sampleBeerEntryIds,
        IReadOnlyCollection<(Guid JudgeId, Guid BeerEntryId)> submittedPairs,
        IReadOnlyDictionary<Guid, string> blindCodeByEntryId)
    {
        var submitted = submittedPairs as ISet<(Guid, Guid)> ?? new HashSet<(Guid, Guid)>(submittedPairs);
        var missing = new List<string>();

        foreach (var beerEntryId in sampleBeerEntryIds)
        {
            var everyActiveJudgeEvaluatedIt = activeJudgeIds.All(judgeId => submitted.Contains((judgeId, beerEntryId)));
            if (!everyActiveJudgeEvaluatedIt)
            {
                missing.Add(blindCodeByEntryId[beerEntryId]);
            }
        }

        return missing;
    }

    /// <summary>FR-042: arithmetic mean of a sample's submitted totals. Shared by CloseTable (all
    /// samples at once) and CorrectEvaluation (a single sample, recomputed after the correction).</summary>
    public static decimal ComputeMean(IReadOnlyCollection<int> totals) =>
        totals.Count == 0 ? 0m : totals.Average(t => (decimal)t);
}
