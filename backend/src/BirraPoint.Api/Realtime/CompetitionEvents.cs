namespace BirraPoint.Api.Realtime;

/// <summary>
/// The closed set of server-to-client event names from contracts/signalr-hub.md §Server → client
/// events. Named constants instead of inline strings so the ~6 stories that emit these can't drift
/// from the contract by typo. Adding a new entry here is a contract amendment (Principle VI).
/// </summary>
public static class CompetitionEvents
{
    public const string CompetitionStateChanged = nameof(CompetitionStateChanged);

    public const string TableOrderFixed = nameof(TableOrderFixed);

    public const string EvaluationCompleted = nameof(EvaluationCompleted);

    public const string TableClosed = nameof(TableClosed);

    public const string DiscrepancyRaised = nameof(DiscrepancyRaised);

    public const string DiscrepancyResolved = nameof(DiscrepancyResolved);

    public const string JudgeRemoved = nameof(JudgeRemoved);

    public const string DispatchProgress = nameof(DispatchProgress);
}
