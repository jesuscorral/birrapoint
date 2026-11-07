using Microsoft.Extensions.Options;

namespace BuildingBlocks.Jobs;

/// <summary>Worker settings, bound from the configuration section passed to
/// <see cref="JobsServiceCollectionExtensions.AddJobs{TContext}"/>.</summary>
public sealed class JobOptions
{
    /// <summary>Smallest accepted <see cref="LeaseDuration"/> / <see cref="SafetyNetPollInterval"/> /
    /// <see cref="MaxBackoff"/>. 1 s keeps the lease renewal timer (<c>LeaseDuration / 3</c>) positive and
    /// lets integration tests use short leases.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

    /// <summary>How long a claimed job stays owned without renewal. The worker renews at a third of this
    /// while the handler runs; a Running job past it is treated as abandoned.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Interval of the worker's periodic sweep (pending claim + expired-lease recovery). Every sweep
    /// runs queries, so a short interval keeps a scale-to-zero database awake around the clock; production
    /// can set this to hours. Enqueues, manual retries and in-process backoff retries wake the worker
    /// directly, so this only bounds crash recovery and cross-replica pickup latency.</summary>
    public TimeSpan SafetyNetPollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Attempts (including the first) before a failing job is marked Failed.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Upper bound of the retry backoff (1 s, 2 s, 4 s, ... doubling per attempt).</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Returns an error message, or null when the options are valid.</summary>
    public string? Validate()
    {
        if (LeaseDuration < MinimumInterval)
        {
            return $"{nameof(LeaseDuration)} must be at least 1 second.";
        }

        if (SafetyNetPollInterval < MinimumInterval)
        {
            return $"{nameof(SafetyNetPollInterval)} must be at least 1 second.";
        }

        if (MaxAttempts < 1)
        {
            return $"{nameof(MaxAttempts)} must be at least 1.";
        }

        if (MaxBackoff < MinimumInterval)
        {
            return $"{nameof(MaxBackoff)} must be at least 1 second.";
        }

        return null;
    }
}

internal sealed class JobOptionsValidator : IValidateOptions<JobOptions>
{
    public ValidateOptionsResult Validate(string? name, JobOptions options) =>
        options.Validate() is { } error ? ValidateOptionsResult.Fail(error) : ValidateOptionsResult.Success;
}
