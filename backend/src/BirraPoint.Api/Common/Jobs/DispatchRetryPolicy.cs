namespace BirraPoint.Api.Common.Jobs;

/// <summary>
/// Pure retry/backoff decision for <see cref="Domain.DispatchJob"/> (R-06). Not specified by an
/// exact number anywhere in the spec beyond "retry with backoff; Failed after max attempts,
/// retryable via API (FR-041)" (data-model.md) — <see cref="MaxAttempts"/> and the backoff curve
/// are an engineering choice, not a contract value.
/// </summary>
public static class DispatchRetryPolicy
{
    public const int MaxAttempts = 5;

    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    public static bool ShouldRetry(int attemptsSoFar) => attemptsSoFar < MaxAttempts;

    /// <summary>Doubles per attempt (1s, 2s, 4s, 8s, ...), capped at <see cref="MaxBackoff"/>.</summary>
    public static TimeSpan BackoffDelay(int attemptsSoFar)
    {
        var seconds = Math.Pow(2, attemptsSoFar);
        return seconds >= MaxBackoff.TotalSeconds ? MaxBackoff : TimeSpan.FromSeconds(seconds);
    }
}
