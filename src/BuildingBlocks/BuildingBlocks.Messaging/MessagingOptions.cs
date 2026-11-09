using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging;

/// <summary>Dispatcher and retention settings, bound from the configuration section passed to
/// <see cref="MessagingServiceCollectionExtensions.AddMessaging{TContext}"/>.</summary>
public sealed class MessagingOptions
{
    /// <summary>Smallest accepted interval; keeps the lease renewal timer (<c>LeaseDuration / 3</c>) positive and
    /// lets integration tests use short leases.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

    /// <summary>When false the hosted dispatcher does nothing (no delivery, no purge). Used by tests that hold
    /// delivery to look at the outbox, and to run an API replica that only publishes.</summary>
    public bool DispatcherEnabled { get; set; } = true;

    /// <summary>How long a claimed row stays owned without renewal; renewed at a third of this while delivering.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Interval of the dispatcher's periodic sweep. Commits, backoff retries and lease expiries wake the
    /// dispatcher directly, so this only bounds crash recovery and cross-replica pickup latency.</summary>
    public TimeSpan SafetyNetPollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Attempts (including the first) before a failing event is marked Dead.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Upper bound of the retry backoff (1 s, 2 s, 4 s, ... doubling per attempt).</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Delivered outbox rows older than this (by OccurredAt) are purged.</summary>
    public TimeSpan OutboxRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Dead outbox rows are kept longer, for diagnosis.</summary>
    public TimeSpan DeadRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Inbox rows older than this (by ProcessedAt) are purged. Must outlast any redelivery and the dead retention (a dead row requeued by hand must still be de-duplicated).</summary>
    public TimeSpan InboxRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>At most one purge pass per interval, run from the dispatcher sweep.</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Rows deleted per statement, so locks stay short.</summary>
    public int PurgeBatchSize { get; set; } = 1000;

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

        if (PurgeInterval < MinimumInterval)
        {
            return $"{nameof(PurgeInterval)} must be at least 1 second.";
        }

        if (PurgeBatchSize < 1)
        {
            return $"{nameof(PurgeBatchSize)} must be at least 1.";
        }

        if (OutboxRetention <= TimeSpan.Zero || DeadRetention <= TimeSpan.Zero)
        {
            return $"{nameof(OutboxRetention)} and {nameof(DeadRetention)} must be positive.";
        }

        if (InboxRetention <= OutboxRetention)
        {
            return $"{nameof(InboxRetention)} must be longer than {nameof(OutboxRetention)}.";
        }

        if (InboxRetention < DeadRetention)
        {
            return $"{nameof(InboxRetention)} must be at least {nameof(DeadRetention)}.";
        }

        // An event can be redelivered until its last attempt ends; the inbox row must still exist then.
        if (InboxRetention <= (MaxBackoff * MaxAttempts) + LeaseDuration)
        {
            return $"{nameof(InboxRetention)} must be longer than {nameof(MaxBackoff)} x {nameof(MaxAttempts)} + {nameof(LeaseDuration)}.";
        }

        return null;
    }
}

internal sealed class MessagingOptionsValidator : IValidateOptions<MessagingOptions>
{
    public ValidateOptionsResult Validate(string? name, MessagingOptions options) =>
        options.Validate() is { } error ? ValidateOptionsResult.Fail(error) : ValidateOptionsResult.Success;
}
