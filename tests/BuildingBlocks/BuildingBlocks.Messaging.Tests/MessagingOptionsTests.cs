namespace BuildingBlocks.Messaging.Tests;

/// <summary>Defaults and validation of the <c>Messaging</c> section (FR-080, FR-090).</summary>
public sealed class MessagingOptionsTests
{
    [Fact]
    public void DefaultsAreValidAndKeepTheDocumentedValues()
    {
        var options = new MessagingOptions();

        Assert.True(options.DispatcherEnabled);
        Assert.Equal(TimeSpan.FromMinutes(2), options.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), options.SafetyNetPollInterval);
        Assert.Equal(5, options.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(60), options.MaxBackoff);
        Assert.Equal(TimeSpan.FromDays(7), options.OutboxRetention);
        Assert.Equal(TimeSpan.FromDays(30), options.DeadRetention);
        Assert.Equal(TimeSpan.FromDays(14), options.InboxRetention);
        Assert.Equal(TimeSpan.FromHours(1), options.PurgeInterval);
        Assert.Equal(1000, options.PurgeBatchSize);
        Assert.Null(options.Validate());
    }

    [Fact]
    public void ShortIntervalsUsedByTestsAreValid()
    {
        var options = new MessagingOptions
        {
            LeaseDuration = TimeSpan.FromSeconds(1),
            SafetyNetPollInterval = TimeSpan.FromSeconds(1),
            MaxBackoff = TimeSpan.FromSeconds(1),
            PurgeInterval = TimeSpan.FromSeconds(1),
        };

        Assert.Null(options.Validate());
    }

    [Fact]
    public void LeaseDurationBelowOneSecondIsRejected() =>
        Assert.Contains("LeaseDuration", new MessagingOptions { LeaseDuration = TimeSpan.FromMilliseconds(999) }.Validate(), StringComparison.Ordinal);

    [Fact]
    public void SafetyNetPollIntervalBelowOneSecondIsRejected() =>
        Assert.Contains("SafetyNetPollInterval", new MessagingOptions { SafetyNetPollInterval = TimeSpan.Zero }.Validate(), StringComparison.Ordinal);

    [Fact]
    public void MaxBackoffBelowOneSecondIsRejected() =>
        Assert.Contains("MaxBackoff", new MessagingOptions { MaxBackoff = TimeSpan.FromMilliseconds(500) }.Validate(), StringComparison.Ordinal);

    [Fact]
    public void PurgeIntervalBelowOneSecondIsRejected() =>
        Assert.Contains("PurgeInterval", new MessagingOptions { PurgeInterval = TimeSpan.Zero }.Validate(), StringComparison.Ordinal);

    [Fact]
    public void MaxAttemptsBelowOneIsRejected() =>
        Assert.Contains("MaxAttempts", new MessagingOptions { MaxAttempts = 0 }.Validate(), StringComparison.Ordinal);

    [Fact]
    public void PurgeBatchSizeBelowOneIsRejected() =>
        Assert.Contains("PurgeBatchSize", new MessagingOptions { PurgeBatchSize = 0 }.Validate(), StringComparison.Ordinal);

    [Fact]
    public void InboxRetentionNotLongerThanOutboxRetentionIsRejected()
    {
        var options = new MessagingOptions { OutboxRetention = TimeSpan.FromDays(14), InboxRetention = TimeSpan.FromDays(14) };

        Assert.Contains("InboxRetention", options.Validate(), StringComparison.Ordinal);
    }

    [Fact]
    public void InboxRetentionNotOutlastingTheLongestRedeliveryWindowIsRejected()
    {
        // 5 attempts x 60 s backoff + 2 min lease = 7 min of redelivery window.
        var options = new MessagingOptions
        {
            OutboxRetention = TimeSpan.FromMinutes(1),
            InboxRetention = TimeSpan.FromMinutes(7),
        };

        Assert.Contains("InboxRetention", options.Validate(), StringComparison.Ordinal);
        options.InboxRetention = TimeSpan.FromMinutes(7) + TimeSpan.FromSeconds(1);
        Assert.Null(options.Validate());
    }

    [Fact]
    public void ValidatorReportsTheProblem()
    {
        var validator = new MessagingOptionsValidator();

        var bad = validator.Validate(null, new MessagingOptions { MaxAttempts = 0 });

        Assert.True(bad.Failed);
        Assert.Contains("MaxAttempts", bad.FailureMessage, StringComparison.Ordinal);
        Assert.True(validator.Validate(null, new MessagingOptions()).Succeeded);
    }
}
