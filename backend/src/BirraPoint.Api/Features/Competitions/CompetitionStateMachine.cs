using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Features.Competitions;

/// <summary>Pure FR-006 gate: forward-only, skip-free, no reverse/same-state transitions.</summary>
public static class CompetitionStateMachine
{
    private static readonly IReadOnlyDictionary<CompetitionState, CompetitionState> ForwardTransitions =
        new Dictionary<CompetitionState, CompetitionState>
        {
            [CompetitionState.Draft] = CompetitionState.Active,
            [CompetitionState.Active] = CompetitionState.InEvaluation,
            [CompetitionState.InEvaluation] = CompetitionState.Finalized,
        };

    public static bool CanTransition(CompetitionState from, CompetitionState to) =>
        ForwardTransitions.TryGetValue(from, out var next) && next == to;
}
