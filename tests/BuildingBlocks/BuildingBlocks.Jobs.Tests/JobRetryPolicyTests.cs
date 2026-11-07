namespace BuildingBlocks.Jobs.Tests;

public sealed class JobRetryPolicyTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    public void ShouldRetryStopsOnceMaxAttemptsIsReached(int attempts, bool expected) =>
        Assert.Equal(expected, JobRetryPolicy.ShouldRetry(attempts, maxAttempts: 5));

    [Fact]
    public void ShouldRetryHonoursAConfiguredLimit()
    {
        Assert.True(JobRetryPolicy.ShouldRetry(1, maxAttempts: 2));
        Assert.False(JobRetryPolicy.ShouldRetry(2, maxAttempts: 2));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    public void BackoffDelayDoublesPerAttempt(int attempts, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), JobRetryPolicy.BackoffDelay(attempts, TimeSpan.FromSeconds(60)));

    [Fact]
    public void BackoffDelayIsCappedSoItNeverGrowsUnbounded() =>
        Assert.Equal(TimeSpan.FromSeconds(60), JobRetryPolicy.BackoffDelay(20, TimeSpan.FromSeconds(60)));

    [Fact]
    public void BackoffDelayHonoursAConfiguredCap() =>
        Assert.Equal(TimeSpan.FromSeconds(3), JobRetryPolicy.BackoffDelay(2, TimeSpan.FromSeconds(3)));
}
