using System.Globalization;

namespace BuildingBlocks.Jobs.Tests;

public sealed class JobOptionsTests
{
    [Fact]
    public void DefaultsAreValidAndKeepTheDocumentedValues()
    {
        var options = new JobOptions();

        Assert.Equal(TimeSpan.FromMinutes(2), options.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), options.SafetyNetPollInterval);
        Assert.Equal(5, options.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(60), options.MaxBackoff);
        Assert.Null(options.Validate());
    }

    [Theory]
    [InlineData("00:00:01", "01:00:00")]
    [InlineData("00:02:00", "00:00:01")]
    [InlineData("00:00:05", "1.00:00:00")]
    public void BoundaryAndProductionValuesAreValid(string lease, string poll)
    {
        var options = new JobOptions
        {
            LeaseDuration = TimeSpan.Parse(lease, CultureInfo.InvariantCulture),
            SafetyNetPollInterval = TimeSpan.Parse(poll, CultureInfo.InvariantCulture),
        };

        Assert.Null(options.Validate());
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:00:00.999")]
    [InlineData("-00:00:05")]
    public void LeaseDurationBelowOneSecondIsRejected(string lease)
    {
        var options = new JobOptions { LeaseDuration = TimeSpan.Parse(lease, CultureInfo.InvariantCulture) };

        Assert.Contains("LeaseDuration", options.Validate(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:00:00.999")]
    [InlineData("-00:00:30")]
    public void SafetyNetPollIntervalBelowOneSecondIsRejected(string poll)
    {
        var options = new JobOptions { SafetyNetPollInterval = TimeSpan.Parse(poll, CultureInfo.InvariantCulture) };

        Assert.Contains("SafetyNetPollInterval", options.Validate(), StringComparison.Ordinal);
    }

    [Fact]
    public void MaxAttemptsBelowOneIsRejected() =>
        Assert.Contains("MaxAttempts", new JobOptions { MaxAttempts = 0 }.Validate(), StringComparison.Ordinal);

    [Fact]
    public void MaxBackoffBelowOneSecondIsRejected() =>
        Assert.Contains("MaxBackoff", new JobOptions { MaxBackoff = TimeSpan.FromMilliseconds(500) }.Validate(), StringComparison.Ordinal);

    [Fact]
    public void ValidatorFailureMessageNamesTheBadSettingWithoutAHostPrefix()
    {
        var validator = new JobOptionsValidator();

        var lease = validator.Validate(null, new JobOptions { LeaseDuration = TimeSpan.Zero });
        var poll = validator.Validate(null, new JobOptions { SafetyNetPollInterval = TimeSpan.Zero });

        Assert.True(lease.Failed);
        Assert.Contains("LeaseDuration", lease.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatch", lease.FailureMessage, StringComparison.Ordinal);
        Assert.True(poll.Failed);
        Assert.Contains("SafetyNetPollInterval", poll.FailureMessage, StringComparison.Ordinal);
        Assert.True(validator.Validate(null, new JobOptions()).Succeeded);
    }
}
