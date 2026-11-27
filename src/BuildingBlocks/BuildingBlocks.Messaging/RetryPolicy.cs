namespace BuildingBlocks.Messaging;

/// <summary>Pure retry/backoff decision for a failed delivery: bounded attempts, exponential backoff.</summary>
internal static class RetryPolicy
{
    public static bool ShouldRetry(int attemptsSoFar, int maxAttempts) => attemptsSoFar < maxAttempts;

    /// <summary>Doubles per failed attempt (1 s after the first, then 2 s, 4 s, ...), capped at
    /// <paramref name="maxBackoff"/>.</summary>
    public static TimeSpan BackoffDelay(int attemptsSoFar, TimeSpan maxBackoff)
    {
        var seconds = Math.Pow(2, Math.Max(attemptsSoFar - 1, 0));
        return seconds >= maxBackoff.TotalSeconds ? maxBackoff : TimeSpan.FromSeconds(seconds);
    }
}
