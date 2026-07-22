using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Features.Evaluations;

/// <summary>
/// Pure FR-022/FR-023/FR-025 rules for submitting a judge evaluation — no EF/DB dependency, so
/// these are unit-testable directly (same "pure rule class beside the DB-touching handler"
/// pattern already used by Features/TastingOrder/TastingOrderRules, Features/Tables/BosFlagRules
/// and CoiDetector). SubmitEvaluationCommandHandler runs these under no explicit lock — the
/// (JudgeId, BeerEntryId) unique index (data-model.md) is what actually backstops a genuine race
/// between two submissions for the same pair, not these gates.
/// </summary>
public static class SubmitEvaluationRules
{
    /// <summary>Section score caps (FR-023/FR-024) — shared by SubmitEvaluationCommandValidator so
    /// the wire-boundary validation and these unit tests can never drift from a single source.</summary>
    public const int AromaMax = 12;

    public const int AppearanceMax = 3;

    public const int FlavorMax = 20;

    public const int MouthfeelMax = 5;

    public const int OverallMax = 10;

    /// <summary>Minimum comment length per section (FR-025).</summary>
    public const int MinCommentLength = 20;

    public static bool CanSubmitInState(CompetitionState competitionState) =>
        competitionState is CompetitionState.InEvaluation;

    /// <summary>
    /// True only when <paramref name="requestedBeerEntryId"/> is the first id in
    /// <paramref name="orderedSampleIds"/> that is not already present in
    /// <paramref name="alreadySubmittedIds"/> (FR-022 — strictly sequential samples). False when
    /// every sample in the fixed order has already been submitted, or when the requested id isn't
    /// part of the fixed order at all.
    /// </summary>
    public static bool IsNextInSequence(
        IReadOnlyList<Guid> orderedSampleIds,
        IReadOnlyCollection<Guid> alreadySubmittedIds,
        Guid requestedBeerEntryId)
    {
        var submitted = alreadySubmittedIds as ISet<Guid> ?? new HashSet<Guid>(alreadySubmittedIds);

        foreach (var sampleId in orderedSampleIds)
        {
            if (!submitted.Contains(sampleId))
            {
                return sampleId == requestedBeerEntryId;
            }
        }

        return false;
    }
}
