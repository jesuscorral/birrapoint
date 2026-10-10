using BirraPoint.Api.Common.Jobs;

namespace BirraPoint.Api.UnitTests.Dispatch;

public sealed class DispatchOptionsTests
{
    [Fact]
    public void Defaults_are_valid_and_keep_the_30s_poll()
    {
        var options = new DispatchOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), options.SafetyNetPollInterval);
        Assert.Null(options.Validate());
    }

    [Theory]
    [InlineData("00:00:01", "01:00:00")]
    [InlineData("00:02:00", "00:00:01")]
    [InlineData("00:00:05", "1.00:00:00")]
    public void Boundary_and_production_values_are_valid(string lease, string poll)
    {
        var options = new DispatchOptions
        {
            LeaseDuration = TimeSpan.Parse(lease),
            SafetyNetPollInterval = TimeSpan.Parse(poll),
        };

        Assert.Null(options.Validate());
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:00:00.999")]
    [InlineData("-00:00:05")]
    public void LeaseDuration_below_one_second_is_rejected(string lease)
    {
        var options = new DispatchOptions { LeaseDuration = TimeSpan.Parse(lease) };

        Assert.Contains("Dispatch:LeaseDuration", options.Validate());
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:00:00.999")]
    [InlineData("-00:00:30")]
    public void SafetyNetPollInterval_below_one_second_is_rejected(string poll)
    {
        var options = new DispatchOptions { SafetyNetPollInterval = TimeSpan.Parse(poll) };

        Assert.Contains("Dispatch:SafetyNetPollInterval", options.Validate());
    }

    [Fact]
    public void Validator_failure_message_names_the_bad_setting()
    {
        var validator = new DispatchOptionsValidator();

        var lease = validator.Validate(null, new DispatchOptions { LeaseDuration = TimeSpan.Zero });
        var poll = validator.Validate(null, new DispatchOptions { SafetyNetPollInterval = TimeSpan.Zero });

        Assert.True(lease.Failed);
        Assert.Contains("Dispatch:LeaseDuration", lease.FailureMessage);
        Assert.True(poll.Failed);
        Assert.Contains("Dispatch:SafetyNetPollInterval", poll.FailureMessage);
        Assert.True(validator.Validate(null, new DispatchOptions()).Succeeded);
    }
}
