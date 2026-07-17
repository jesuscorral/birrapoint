using BirraPoint.Api.Common.Jobs;

namespace BirraPoint.Api.UnitTests.Common.Jobs;

public sealed class DispatchRetryPolicyTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    public void ShouldRetry_stops_once_max_attempts_is_reached(int attempts, bool expected)
    {
        Assert.Equal(expected, DispatchRetryPolicy.ShouldRetry(attempts));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    public void BackoffDelay_doubles_per_attempt(int attempts, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), DispatchRetryPolicy.BackoffDelay(attempts));
    }

    [Fact]
    public void BackoffDelay_is_capped_so_it_never_grows_unbounded()
    {
        var delay = DispatchRetryPolicy.BackoffDelay(20);

        Assert.Equal(DispatchRetryPolicy.MaxBackoff, delay);
    }
}
