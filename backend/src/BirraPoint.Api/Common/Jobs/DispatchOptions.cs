namespace BirraPoint.Api.Common.Jobs;

/// <summary>Bound from the <c>Dispatch</c> configuration section (T129, T132).</summary>
public sealed class DispatchOptions
{
    public const string SectionName = "Dispatch";

    /// <summary>Smallest accepted <see cref="LeaseDuration"/> / <see cref="SafetyNetPollInterval"/>.
    /// 1 s keeps the lease renewal timer (<c>LeaseDuration / 3</c>) positive and lets the
    /// concurrency integration tests use short leases.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

    /// <summary>How long a claimed job stays owned without renewal. The worker renews at a third of
    /// this while the handler runs; a Running job past it is treated as abandoned.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Interval of the worker's periodic sweep (pending claim + expired-lease recovery).
    /// Every sweep runs queries, so a short interval keeps a scale-to-zero database (Neon, T132)
    /// awake around the clock; production sets this to hours (<c>Dispatch__SafetyNetPollInterval</c>).
    /// Enqueues, manual retries and in-process backoff retries wake the worker directly, so this
    /// only bounds crash recovery and cross-replica pickup latency.</summary>
    public TimeSpan SafetyNetPollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Returns an error message, or null when the options are valid.</summary>
    public string? Validate()
    {
        if (LeaseDuration < MinimumInterval)
        {
            return "Dispatch:LeaseDuration must be at least 1 second.";
        }

        if (SafetyNetPollInterval < MinimumInterval)
        {
            return "Dispatch:SafetyNetPollInterval must be at least 1 second.";
        }

        return null;
    }
}
