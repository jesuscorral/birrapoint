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
    public void IsExactPermutation_true_for_a_reordering_of_the_same_ids()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        Assert.True(TastingOrderRules.IsExactPermutation([c, a, b], [a, b, c]));
    }

    [Fact]
    public void IsExactPermutation_false_when_an_id_is_missing()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.False(TastingOrderRules.IsExactPermutation([a], [a, b]));
    }

    [Fact]
    public void IsExactPermutation_false_when_an_extra_id_is_present()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var extra = Guid.NewGuid();

        Assert.False(TastingOrderRules.IsExactPermutation([a, b, extra], [a, b]));
    }

    [Fact]
    public void IsExactPermutation_false_when_the_submitted_list_has_a_duplicate()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.False(TastingOrderRules.IsExactPermutation([a, a], [a, b]));
    }

    [Fact]
    public void IsExactPermutation_true_for_an_empty_table()
    {
        Assert.True(TastingOrderRules.IsExactPermutation([], []));
    }

    // ---- EnsureOrderCanBeFixed: one-shot semantics ---------------------------------------------

    [Fact]
    public void EnsureOrderCanBeFixed_throws_order_already_fixed_with_the_fixer_name_when_already_set()
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
    public void EnsureOrderCanBeFixed_throws_invalid_state_transition_outside_active_or_in_evaluation(
        CompetitionState state)
    {
        var exception = Assert.Throws<DomainException>(() =>
            TastingOrderRules.EnsureOrderCanBeFixed(isAlreadyFixed: false, fixedByDisplayName: null, state));

        Assert.Equal(DomainErrorType.InvalidStateTransition, exception.ErrorType);
    }

    [Theory]
    [InlineData(CompetitionState.Active)]
    [InlineData(CompetitionState.InEvaluation)]
    public void EnsureOrderCanBeFixed_succeeds_while_active_or_in_evaluation(CompetitionState state)
    {
        var exception = Record.Exception(() =>
            TastingOrderRules.EnsureOrderCanBeFixed(isAlreadyFixed: false, fixedByDisplayName: null, state));

        Assert.Null(exception);
    }
}
