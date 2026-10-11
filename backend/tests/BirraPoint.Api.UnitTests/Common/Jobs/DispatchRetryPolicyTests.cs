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
    public void ShouldRetryStopsOnceMaxAttemptsIsReached(int attempts, bool expected)
    {
        Assert.Equal(expected, DispatchRetryPolicy.ShouldRetry(attempts));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    public void BackoffDelayDoublesPerAttempt(int attempts, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), DispatchRetryPolicy.BackoffDelay(attempts));
    }

    [Fact]
    public void BackoffDelayIsCappedSoItNeverGrowsUnbounded()
    {
        var delay = DispatchRetryPolicy.BackoffDelay(20);

        Assert.Equal(DispatchRetryPolicy.MaxBackoff, delay);
    }
}
