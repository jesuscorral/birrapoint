using System.Security.Claims;
using System.Text.Json;
using BirraPoint.Api.Common.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
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
    private static ServiceProvider BuildServices(Dictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());
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
        => (await SendForContextAsync(pipeline, services, sub, policy)).Response.StatusCode;

    private static async Task<DefaultHttpContext> SendForContextAsync(
        RequestDelegate pipeline, IServiceProvider services, string? sub, string? policy)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        if (sub is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", sub)], "Test"));
        }

        var metadata = policy is null
            ? new EndpointMetadataCollection()
            : new EndpointMetadataCollection(new EnableRateLimitingAttribute(policy));
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test-endpoint"));

        await pipeline(context);
        return context;
    }

    [Fact]
    public void RejectedRequestsGet429()
    {
        using var services = BuildServices();

        var options = services.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

        Assert.Equal(StatusCodes.Status429TooManyRequests, options.RejectionStatusCode);
    }

    [Fact]
    public async Task ConfiguredPermitLimitIsHonored()
    {
        await using var services = BuildServices(new() { ["RateLimiting:NotifyJudges:PermitLimit"] = "1" });
        var pipeline = BuildPipeline(services);

        Assert.Equal(StatusCodes.Status200OK,
            await SendAsync(pipeline, services, "organizer-a", RateLimitingExtensions.NotifyJudgesPolicy));
        Assert.Equal(StatusCodes.Status429TooManyRequests,
            await SendAsync(pipeline, services, "organizer-a", RateLimitingExtensions.NotifyJudgesPolicy));
    }

    [Fact]
    public async Task ConfiguredPermitLimitAboveDefaultAllowsMoreRequests()
    {
        await using var services = BuildServices(new() { ["RateLimiting:NotifyJudges:PermitLimit"] = "5" });
        var pipeline = BuildPipeline(services);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(StatusCodes.Status200OK,
                await SendAsync(pipeline, services, "organizer-a", RateLimitingExtensions.NotifyJudgesPolicy));
        }
    }

    [Fact]
    public async Task RejectionIsAnRfc7807RateLimitedProblemWithRetryAfter()
    {
        await using var services = BuildServices(new() { ["RateLimiting:NotifyJudges:PermitLimit"] = "1" });
        var pipeline = BuildPipeline(services);
        await SendAsync(pipeline, services, "organizer-a", RateLimitingExtensions.NotifyJudgesPolicy);

        var rejected = await SendForContextAsync(pipeline, services, "organizer-a", RateLimitingExtensions.NotifyJudgesPolicy);

        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.Response.StatusCode);
        Assert.StartsWith("application/problem+json", rejected.Response.ContentType, StringComparison.Ordinal);
        Assert.True(int.TryParse(rejected.Response.Headers.RetryAfter.ToString(), out var retryAfter));
        Assert.InRange(retryAfter, 1, (int)RateLimitingExtensions.DefaultWindow.TotalSeconds);

        rejected.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(rejected.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("urn:birrapoint:rate-limited", body.RootElement.GetProperty("type").GetString());
        Assert.Equal(StatusCodes.Status429TooManyRequests, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task RequestsWithoutASubClaimShareNoBucketAndAreNotThrottled()
    {
        await using var services = BuildServices(new() { ["RateLimiting:NotifyJudges:PermitLimit"] = "1" });
        var pipeline = BuildPipeline(services);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(StatusCodes.Status200OK,
                await SendAsync(pipeline, services, sub: null, RateLimitingExtensions.NotifyJudgesPolicy));
        }
    }

    [Fact]
    public async Task DefaultsAreTwoRequestsPerThirtySecondsWhenUnconfigured()
    {
        await using var services = BuildServices();

        var settings = services.GetRequiredService<IOptions<NotifyJudgesRateLimitOptions>>().Value;

        Assert.Equal(RateLimitingExtensions.DefaultPermitLimit, settings.PermitLimit);
        Assert.Equal(RateLimitingExtensions.DefaultWindow.TotalSeconds, settings.WindowSeconds);
    }

    [Fact]
    public async Task NotifyJudgesAllowsThePermitLimitThenRejectsTheNextRequestInTheSameWindow()
    {
        await using var services = BuildServices();
        var pipeline = BuildPipeline(services);

        var statuses = new List<int>();
        for (var i = 0; i < RateLimitingExtensions.DefaultPermitLimit + 1; i++)
        {
            statuses.Add(await SendAsync(pipeline, services, "organizer-a", RateLimitingExtensions.NotifyJudgesPolicy));
        }

        Assert.All(statuses.Take(RateLimitingExtensions.DefaultPermitLimit), s => Assert.Equal(StatusCodes.Status200OK, s));
        Assert.Equal(StatusCodes.Status429TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task NotifyJudgesBudgetIsPartitionedPerOrganizer()
    {
        await using var services = BuildServices();
        var pipeline = BuildPipeline(services);

        for (var i = 0; i < RateLimitingExtensions.DefaultPermitLimit; i++)
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
