namespace BuildingBlocks.Messaging.Tests;

/// <summary>Bounded attempts and exponential backoff for failed deliveries (FR-080).</summary>
public sealed class RetryPolicyTests
{
    [Theory]
    [InlineData(1, 5, true)]
    [InlineData(4, 5, true)]
    [InlineData(5, 5, false)]
    [InlineData(6, 5, false)]
    [InlineData(1, 1, false)]
    public void RetriesBelowTheLimitAndGoesDeadAtIt(int attemptsSoFar, int maxAttempts, bool retry) =>
        Assert.Equal(retry, RetryPolicy.ShouldRetry(attemptsSoFar, maxAttempts));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(6, 32)]
    public void BackoffDoublesFromOneSecond(int attemptsSoFar, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RetryPolicy.BackoffDelay(attemptsSoFar, TimeSpan.FromSeconds(60)));

    [Theory]
    [InlineData(7)]
    [InlineData(40)]
    [InlineData(2000)]
    public void BackoffIsCappedAtMaxBackoff(int attemptsSoFar) =>
        Assert.Equal(TimeSpan.FromSeconds(60), RetryPolicy.BackoffDelay(attemptsSoFar, TimeSpan.FromSeconds(60)));
}
