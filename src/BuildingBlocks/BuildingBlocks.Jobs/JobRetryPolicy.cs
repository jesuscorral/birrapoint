namespace BuildingBlocks.Jobs;

/// <summary>Pure retry/backoff decision for a failed attempt: bounded attempts, exponential backoff.</summary>
internal static class JobRetryPolicy
{
    public static bool ShouldRetry(int attemptsSoFar, int maxAttempts) => attemptsSoFar < maxAttempts;

    /// <summary>Doubles per attempt (1s, 2s, 4s, 8s, ...), capped at <paramref name="maxBackoff"/>.</summary>
    public static TimeSpan BackoffDelay(int attemptsSoFar, TimeSpan maxBackoff)
    {
        var seconds = Math.Pow(2, attemptsSoFar);
        return seconds >= maxBackoff.TotalSeconds ? maxBackoff : TimeSpan.FromSeconds(seconds);
    }
}
