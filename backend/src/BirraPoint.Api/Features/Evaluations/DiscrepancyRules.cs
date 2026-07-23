namespace BirraPoint.Api.Features.Evaluations;

/// <summary>
/// Pure FR-031 rule — no EF/DB dependency, so it's unit-testable directly (same "pure rule class
/// beside the DB-touching handler" pattern already used by SubmitEvaluationRules,
/// Features/TastingOrder/TastingOrderRules, Features/Tables/BosFlagRules and CoiDetector).
/// </summary>
public static class DiscrepancyRules
{
    /// <summary>Totals more than this many points apart, for the same sample, are a discrepancy
    /// (FR-031). Exactly 7 apart is still consensus.</summary>
    public const int Threshold = 7;

    /// <summary>
    /// A judge is involved iff their total differs by more than <see cref="Threshold"/> from at
    /// least one other judge's total for the same sample — not every judge is necessarily
    /// involved just because the group's overall spread is large (spec edge case, ≥3 judges).
    /// </summary>
    public static IReadOnlySet<Guid> ComputeInvolvedJudgeIds(IReadOnlyDictionary<Guid, int> totalsByJudge)
    {
        var involved = new HashSet<Guid>();
        var entries = totalsByJudge.ToList();

        for (var i = 0; i < entries.Count; i++)
        {
            for (var j = i + 1; j < entries.Count; j++)
            {
                if (Math.Abs(entries[i].Value - entries[j].Value) > Threshold)
                {
                    involved.Add(entries[i].Key);
                    involved.Add(entries[j].Key);
                }
            }
        }

        return involved;
    }
}
