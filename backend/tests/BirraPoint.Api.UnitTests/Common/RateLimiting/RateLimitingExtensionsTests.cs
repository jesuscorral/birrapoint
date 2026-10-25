using System.Security.Claims;
using BirraPoint.Api.Common.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BirraPoint.Api.UnitTests.Common.RateLimiting;

/// <summary>
/// Drives the real rate-limiter middleware registered by <see cref="RateLimitingExtensions.ConfigureRateLimiting"/>
/// in-process (no host, no network): each request is a <see cref="DefaultHttpContext"/> carrying the
/// endpoint metadata that <c>RequireRateLimiting</c> would attach.
/// </summary>
public sealed class RateLimitingExtensionsTests
{
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureRateLimiting();
        return services.BuildServiceProvider();
    }

    private static RequestDelegate BuildPipeline(IServiceProvider services)
    {
        var app = new ApplicationBuilder(services);
        app.UseRateLimiter();
        app.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        return app.Build();
    }

    private static async Task<int> SendAsync(RequestDelegate pipeline, IServiceProvider services, string? sub, string? policy)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        if (sub is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", sub)], "Test"));
        }

        var metadata = policy is null
            ? new EndpointMetadataCollection()
            : new EndpointMetadataCollection(new EnableRateLimitingAttribute(policy));
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test-endpoint"));

        await pipeline(context);
        return context.Response.StatusCode;
    }

    [Fact]
    public void RejectedRequestsGet429()
    {
        using var services = BuildServices();

        var options = services.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

        Assert.Equal(StatusCodes.Status429TooManyRequests, options.RejectionStatusCode);
    }

    [Fact]
    public void NotifyJudgesPolicyAllowsTwoRequestsPerThirtySeconds()
    {
        Assert.Equal("NotifyJudges", RateLimitingExtensions.NotifyJudgesPolicy);
        Assert.Equal(2, RateLimitingExtensions.NotifyJudgesPermitLimit);
        Assert.Equal(TimeSpan.FromSeconds(30), RateLimitingExtensions.NotifyJudgesWindow);
    }

    [Fact]
    public async Task NotifyJudgesAllowsThePermitLimitThenRejectsTheNextRequestInTheSameWindow()
    {
        await using var services = BuildServices();
        var pipeline = BuildPipeline(services);

        var statuses = new List<int>();
        for (var i = 0; i < RateLimitingExtensions.NotifyJudgesPermitLimit + 1; i++)
        {
            statuses.Add(await SendAsync(pipeline, services, "organizer-a", RateLimitingExtensions.NotifyJudgesPolicy));
        }

        Assert.All(statuses.Take(RateLimitingExtensions.NotifyJudgesPermitLimit), s => Assert.Equal(StatusCodes.Status200OK, s));
        Assert.Equal(StatusCodes.Status429TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task NotifyJudgesBudgetIsPartitionedPerOrganizer()
    {
        await using var services = BuildServices();
        var pipeline = BuildPipeline(services);

        for (var i = 0; i < RateLimitingExtensions.NotifyJudgesPermitLimit; i++)
        {
            await SendAsync(pipeline, services, "organizer-a", RateLimitingExtensions.NotifyJudgesPolicy);
        }
        Assert.Equal(StatusCodes.Status429TooManyRequests,
            await SendAsync(pipeline, services, "organizer-a", RateLimitingExtensions.NotifyJudgesPolicy));

        // A different organizer must not be throttled by organizer-a's usage.
        Assert.Equal(StatusCodes.Status200OK,
            await SendAsync(pipeline, services, "organizer-b", RateLimitingExtensions.NotifyJudgesPolicy));
    }

    [Fact]
    public async Task EndpointsWithoutARateLimitPolicyAreNeverThrottled()
    {
        await using var services = BuildServices();
        var pipeline = BuildPipeline(services);

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(StatusCodes.Status200OK, await SendAsync(pipeline, services, "organizer-a", policy: null));
        }
    }
}
