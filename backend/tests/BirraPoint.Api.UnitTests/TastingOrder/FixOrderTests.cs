using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.TastingOrder;

namespace BirraPoint.Api.UnitTests.TastingOrder;

/// <summary>
/// T050: pure FixOrder rules (US6-4) — permutation validation and the one-shot/state-gating
/// checks the handler runs under the table's row lock. Exercises <see cref="TastingOrderRules"/>
/// directly (same "pure rule class beside the DB-touching handler" pattern already used by
/// Features/Tables/BosFlagRules and CoiDetector) rather than the full MediatR handler, which
/// needs a real transaction/row lock against Postgres (Testcontainers, integration-test
/// territory per the constitution's ban on EF InMemory) — OrderApiTests.cs (T051) covers the
/// end-to-end handler behavior, including the concurrent-fixer race.
/// </summary>
public sealed class FixOrderTests
{
    // ---- IsExactPermutation --------------------------------------------------------------------

    [Fact]
    public void IsExactPermutationTrueForAReorderingOfTheSameIds()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        Assert.True(TastingOrderRules.IsExactPermutation([c, a, b], [a, b, c]));
    }

    [Fact]
    public void IsExactPermutationFalseWhenAnIdIsMissing()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.False(TastingOrderRules.IsExactPermutation([a], [a, b]));
    }

    [Fact]
    public void IsExactPermutationFalseWhenAnExtraIdIsPresent()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var extra = Guid.NewGuid();

        Assert.False(TastingOrderRules.IsExactPermutation([a, b, extra], [a, b]));
    }

    [Fact]
    public void IsExactPermutationFalseWhenTheSubmittedListHasADuplicate()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.False(TastingOrderRules.IsExactPermutation([a, a], [a, b]));
    }

    [Fact]
    public void IsExactPermutationTrueForAnEmptyTable()
    {
        Assert.True(TastingOrderRules.IsExactPermutation([], []));
    }

    // ---- EnsureOrderCanBeFixed: one-shot semantics ---------------------------------------------

    [Fact]
    public void EnsureOrderCanBeFixedThrowsOrderAlreadyFixedWithTheFixerNameWhenAlreadySet()
    {
        var exception = Assert.Throws<DomainException>(() =>
            TastingOrderRules.EnsureOrderCanBeFixed(isAlreadyFixed: true, "Ana Gomez", CompetitionState.Active));

        Assert.Equal(DomainErrorType.OrderAlreadyFixed, exception.ErrorType);
        Assert.Equal("Ana Gomez", exception.Extensions["fixedBy"]);
    }

    // ---- EnsureOrderCanBeFixed: state gating ----------------------------------------------------

    [Theory]
    [InlineData(CompetitionState.Draft)]
    [InlineData(CompetitionState.Finalized)]
    public void EnsureOrderCanBeFixedThrowsInvalidStateTransitionOutsideActiveOrInEvaluation(
        CompetitionState state)
    {
        var exception = Assert.Throws<DomainException>(() =>
            TastingOrderRules.EnsureOrderCanBeFixed(isAlreadyFixed: false, fixedByDisplayName: null, state));

        Assert.Equal(DomainErrorType.InvalidStateTransition, exception.ErrorType);
    }

    [Theory]
    [InlineData(CompetitionState.Active)]
    [InlineData(CompetitionState.InEvaluation)]
    public void EnsureOrderCanBeFixedSucceedsWhileActiveOrInEvaluation(CompetitionState state)
    {
        var exception = Record.Exception(() =>
            TastingOrderRules.EnsureOrderCanBeFixed(isAlreadyFixed: false, fixedByDisplayName: null, state));

        Assert.Null(exception);
    }
}
