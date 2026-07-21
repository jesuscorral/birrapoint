using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Features.TastingOrder;

/// <summary>
/// Pure FR-020/US6-4 rules for fixing a table's tasting order — no EF/DB dependency, so these are
/// unit-testable directly (same "pure rule class beside the DB-touching handler" pattern already
/// used by Features/Tables/BosFlagRules and CoiDetector).
/// </summary>
public static class TastingOrderRules
{
    /// <summary>True only when <paramref name="submitted"/> has no duplicates and is exactly the
    /// same set as <paramref name="currentSampleIds"/> (same count, same elements).</summary>
    public static bool IsExactPermutation(IReadOnlyList<Guid> submitted, IReadOnlyList<Guid> currentSampleIds)
    {
        if (submitted.Count != currentSampleIds.Count)
        {
            return false;
        }

        var distinctSubmitted = new HashSet<Guid>(submitted);
        if (distinctSubmitted.Count != submitted.Count)
        {
            return false;
        }

        return distinctSubmitted.SetEquals(currentSampleIds);
    }

    public static bool CanFixOrderInState(CompetitionState competitionState) =>
        competitionState is CompetitionState.Active or CompetitionState.InEvaluation;

    /// <summary>
    /// Called by FixOrderCommandHandler under the table's row lock (one-shot guarantee, US6-4 /
    /// Clarification Q1). Throws <see cref="DomainErrorType.OrderAlreadyFixed"/> — with the
    /// fixer's display name as the `fixedBy` extension (contracts/rest-api.md) — when the order
    /// was already fixed, or <see cref="DomainErrorType.InvalidStateTransition"/> when the
    /// competition isn't Active/InEvaluation.
    /// </summary>
    public static void EnsureOrderCanBeFixed(bool isAlreadyFixed, string? fixedByDisplayName, CompetitionState competitionState)
    {
        if (isAlreadyFixed)
        {
            throw new DomainException(
                DomainErrorType.OrderAlreadyFixed,
                "The tasting order for this table has already been fixed.",
                new Dictionary<string, object?> { ["fixedBy"] = fixedByDisplayName });
        }

        if (!CanFixOrderInState(competitionState))
        {
            throw new DomainException(
                DomainErrorType.InvalidStateTransition,
                "Tasting order can only be fixed while the competition is Active or InEvaluation.");
        }
    }
}
