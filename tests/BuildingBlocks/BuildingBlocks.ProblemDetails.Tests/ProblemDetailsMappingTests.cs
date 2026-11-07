using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.ProblemDetails.Tests;

public sealed class ProblemDetailsMappingTests
{
    private static readonly string[] Conflicts = ["judge-1"];

    private static ServiceProvider Build(params Action<ProblemDetailsMappingBuilder>[] registrations)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        foreach (var registration in registrations)
        {
            services.AddProblemDetailsMapping(registration);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>Runs the registered handlers in order until one returns true, like the exception-handler middleware.</summary>
    private static async Task<(DefaultHttpContext Context, JsonElement Body)> RunAsync(ServiceProvider provider, Exception exception)
    {
        var context = new DefaultHttpContext { RequestServices = provider, Response = { Body = new MemoryStream() } };
        foreach (var handler in provider.GetServices<IExceptionHandler>())
        {
            if (await handler.TryHandleAsync(context, exception, TestContext.Current.CancellationToken))
            {
                break;
            }
        }

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var document = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        return (context, document.RootElement.Clone());
    }

    [Fact]
    public async Task WritesTheMappedProblemWithStatusContentTypeAndExtensions()
    {
        using var provider = Build(map => map.Map<InvalidOperationException>(_ => new ProblemMapping(
            "urn:test:first", StatusCodes.Status409Conflict, "First failed", "the detail",
            new Dictionary<string, object?> { ["conflicts"] = Conflicts, ["count"] = 2 })));

        var (context, body) = await RunAsync(provider, new InvalidOperationException());

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal("urn:test:first", body.GetProperty("type").GetString());
        Assert.Equal("First failed", body.GetProperty("title").GetString());
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Equal("the detail", body.GetProperty("detail").GetString());
        Assert.Equal("judge-1", body.GetProperty("conflicts")[0].GetString());
        Assert.Equal(2, body.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task PassesTheExceptionThroughToTheProblemDetailsContext()
    {
        var seen = new List<Exception>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = ctx => seen.Add(ctx.Exception!));
        services.AddProblemDetailsMapping(map => map.Map<InvalidOperationException>(_ => new ProblemMapping("urn:test:first", 400, "t")));
        using var provider = services.BuildServiceProvider();
        var exception = new InvalidOperationException();

        await RunAsync(provider, exception);

        Assert.Same(exception, Assert.Single(seen));
    }

    [Fact]
    public async Task FirstRegisteredMatchWins()
    {
        using var provider = Build(map =>
        {
            map.Map<Exception>(_ => new ProblemMapping("urn:test:broad", 400, "broad"));
            map.Map<InvalidOperationException>(_ => new ProblemMapping("urn:test:specific", 409, "specific"));
        });

        var (context, body) = await RunAsync(provider, new InvalidOperationException());

        Assert.Equal("urn:test:broad", body.GetProperty("type").GetString());
        Assert.Equal(400, context.Response.StatusCode);
    }

    [Fact]
    public async Task PicksTheMappingWhoseExceptionTypeMatches()
    {
        using var provider = Build(map =>
        {
            map.Map<InvalidOperationException>(_ => new ProblemMapping("urn:test:first", 409, "first"));
            map.Map<NotSupportedException>(_ => new ProblemMapping("urn:test:second", 422, "second"));
        });

        var (_, body) = await RunAsync(provider, new NotSupportedException());

        Assert.Equal("urn:test:second", body.GetProperty("type").GetString());
    }

    [Fact]
    public async Task MapProblemWritesTheProvidedProblemDetailsInstance()
    {
        using var provider = Build(map => map.MapProblem<InvalidOperationException>(_ => new ValidationProblemDetails(
            new Dictionary<string, string[]> { ["name"] = ["required"] })
        {
            Type = "urn:test:validation",
            Title = "Validation failed",
            Status = StatusCodes.Status400BadRequest,
        }));

        var (context, body) = await RunAsync(provider, new InvalidOperationException());

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("required", body.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task UnmappedExceptionsFallToAGeneric500WithoutLeakingDetails()
    {
        using var provider = Build(map => map.Map<InvalidOperationException>(_ => new ProblemMapping("urn:test:first", 409, "first")));

        var (context, body) = await RunAsync(provider, new TimeoutException("password=hunter2"));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("An unexpected error occurred.", body.GetProperty("title").GetString());
        Assert.DoesNotContain("hunter2", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void RegistersTheFallbackHandlerLast()
    {
        using var provider = Build(map => map.Map<InvalidOperationException>(_ => new ProblemMapping("urn:test:first", 409, "first")));

        var handlers = provider.GetServices<IExceptionHandler>().Select(h => h.GetType().Name).ToList();

        Assert.Equal(["MappedExceptionHandler", "FallbackExceptionHandler"], handlers);
    }

    [Fact]
    public async Task RepeatedRegistrationAppendsMappingsAndKeepsASingleFallbackLast()
    {
        using var provider = Build(
            map => map.Map<InvalidOperationException>(_ => new ProblemMapping("urn:test:first", 409, "first")),
            map => map.Map<NotSupportedException>(_ => new ProblemMapping("urn:test:second", 422, "second")));

        var handlers = provider.GetServices<IExceptionHandler>().Select(h => h.GetType().Name).ToList();
        var (_, first) = await RunAsync(provider, new InvalidOperationException());
        var (_, second) = await RunAsync(provider, new NotSupportedException());

        Assert.Equal(["MappedExceptionHandler", "FallbackExceptionHandler"], handlers);
        Assert.Equal("urn:test:first", first.GetProperty("type").GetString());
        Assert.Equal("urn:test:second", second.GetProperty("type").GetString());
    }

    [Fact]
    public async Task MappingAfterTheFirstHandledExceptionThrows()
    {
        using var provider = Build(map => map.Map<InvalidOperationException>(_ => new ProblemMapping("urn:test:first", 409, "first")));
        await RunAsync(provider, new InvalidOperationException());
        var builder = provider.GetRequiredService<ProblemDetailsMappingBuilder>();

        var map = Assert.Throws<InvalidOperationException>(() =>
            builder.Map<NotSupportedException>(_ => new ProblemMapping("urn:test:late", 422, "late")));
        var mapProblem = Assert.Throws<InvalidOperationException>(() =>
            builder.MapProblem<NotSupportedException>(_ => new Microsoft.AspNetCore.Mvc.ProblemDetails()));

        Assert.Contains("frozen", map.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("frozen", mapProblem.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConcurrentHandlingIsSafe()
    {
        using var provider = Build(map => map.Map<InvalidOperationException>(_ => new ProblemMapping("urn:test:first", 409, "first")));

        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(
            async () => (await RunAsync(provider, new InvalidOperationException())).Context.Response.StatusCode,
            TestContext.Current.CancellationToken)));

        Assert.All(results, status => Assert.Equal(StatusCodes.Status409Conflict, status));
    }

    [Fact]
    public async Task MapProblemFactoryReturningNullThrowsNamingTheExceptionType()
    {
        using var provider = Build(map => map.MapProblem<InvalidOperationException>(_ => null!));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(provider, new InvalidOperationException()));

        Assert.Contains(nameof(InvalidOperationException), thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MapFactoryReturningNullThrowsNamingTheExceptionType()
    {
        using var provider = Build(map => map.Map<NotSupportedException>(_ => null!));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(provider, new NotSupportedException()));

        Assert.Contains(nameof(NotSupportedException), thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNullArguments()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddProblemDetailsMapping(null!));
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddProblemDetailsMapping(_ => { }));
    }
}
