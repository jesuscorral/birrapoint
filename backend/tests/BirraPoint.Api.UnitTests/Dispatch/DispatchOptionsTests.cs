using System.Globalization;
using BirraPoint.Api.Common.Jobs;

namespace BirraPoint.Api.UnitTests.Dispatch;

public sealed class DispatchOptionsTests
{
    [Fact]
    public void DefaultsAreValidAndKeepThe30sPoll()
    {
        var options = new DispatchOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), options.SafetyNetPollInterval);
        Assert.Null(options.Validate());
    }

    [Theory]
    [InlineData("00:00:01", "01:00:00")]
    [InlineData("00:02:00", "00:00:01")]
    [InlineData("00:00:05", "1.00:00:00")]
    public void BoundaryAndProductionValuesAreValid(string lease, string poll)
    {
        var options = new DispatchOptions
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
        var options = new DispatchOptions { LeaseDuration = TimeSpan.Parse(lease, CultureInfo.InvariantCulture) };

        Assert.Contains("Dispatch:LeaseDuration", options.Validate(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:00:00.999")]
    [InlineData("-00:00:30")]
    public void SafetyNetPollIntervalBelowOneSecondIsRejected(string poll)
    {
        var options = new DispatchOptions { SafetyNetPollInterval = TimeSpan.Parse(poll, CultureInfo.InvariantCulture) };

        Assert.Contains("Dispatch:SafetyNetPollInterval", options.Validate(), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatorFailureMessageNamesTheBadSetting()
    {
        var validator = new DispatchOptionsValidator();

        var lease = validator.Validate(null, new DispatchOptions { LeaseDuration = TimeSpan.Zero });
        var poll = validator.Validate(null, new DispatchOptions { SafetyNetPollInterval = TimeSpan.Zero });

        Assert.True(lease.Failed);
        Assert.Contains("Dispatch:LeaseDuration", lease.FailureMessage, StringComparison.Ordinal);
        Assert.True(poll.Failed);
        Assert.Contains("Dispatch:SafetyNetPollInterval", poll.FailureMessage, StringComparison.Ordinal);
        Assert.True(validator.Validate(null, new DispatchOptions()).Succeeded);
    }
}
