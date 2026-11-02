using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Features.Competitions.Features.ChangeCompetitionState;

namespace BirraPoint.Api.UnitTests.Modules.Organization.Competitions.Features.ChangeCompetitionState;

/// <summary>CompetitionStateMachine gates per FR-006 — forward-only, skip-free, reverse/same-state rejected.</summary>
public sealed class CompetitionStateMachineTests
{
    [Theory]
    [InlineData(CompetitionState.Draft, CompetitionState.Active, true)]
    [InlineData(CompetitionState.Active, CompetitionState.InEvaluation, true)]
    [InlineData(CompetitionState.InEvaluation, CompetitionState.Finalized, true)]
    [InlineData(CompetitionState.Draft, CompetitionState.InEvaluation, false)]
    [InlineData(CompetitionState.Draft, CompetitionState.Finalized, false)]
    [InlineData(CompetitionState.Active, CompetitionState.Finalized, false)]
    [InlineData(CompetitionState.Active, CompetitionState.Draft, false)]
    [InlineData(CompetitionState.InEvaluation, CompetitionState.Draft, false)]
    [InlineData(CompetitionState.InEvaluation, CompetitionState.Active, false)]
    [InlineData(CompetitionState.Finalized, CompetitionState.Active, false)]
    [InlineData(CompetitionState.Draft, CompetitionState.Draft, false)]
    [InlineData(CompetitionState.Finalized, CompetitionState.Finalized, false)]
    public void StateMachineAllowsOnlyForwardSkipFreeTransitions(
        CompetitionState from, CompetitionState to, bool expected)
    {
        Assert.Equal(expected, CompetitionStateMachine.CanTransition(from, to));
    }
}
