using System.Text.Json;
using BuildingBlocks.ProblemDetails;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Validation.MediatR.Tests;

public sealed class ValidationMappingTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetailsMapping(map => map.MapValidation(o =>
        {
            o.Type = "urn:test:validation";
            o.Title = "Validation failed";
        }));
        return services.BuildServiceProvider();
    }

    /// <summary>Runs the registered handlers in order until one returns true, like the exception-handler middleware.</summary>
    private static async Task<bool> HandleAsync(HttpContext context, Exception exception)
    {
        using var provider = Build();
        context.RequestServices = provider;
        foreach (var handler in provider.GetServices<IExceptionHandler>())
        {
            if (await handler.TryHandleAsync(context, exception, CancellationToken.None))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public async Task WritesA400WithTheConfiguredTypeTitleAndAFieldErrorMap()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var exception = new ValidationException(new[]
        {
            new ValidationFailure("Name", "Name is required."),
            new ValidationFailure("Name", "Name must be under 200 characters."),
            new ValidationFailure("EndDate", "EndDate must be on or after StartDate."),
        });

        var handled = await HandleAsync(context, exception);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType, StringComparison.Ordinal);

        var body = await ReadBodyAsync(context);
        Assert.Equal("urn:test:validation", body.GetProperty("type").GetString());
        Assert.Equal("Validation failed", body.GetProperty("title").GetString());
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        var errors = body.GetProperty("errors");
        // Keys are camelCase ("name") even though FluentValidation PropertyName is PascalCase:
        // System.Text.Json naming policy never touches Dictionary<string,T> keys on its own.
        Assert.Equal(2, errors.GetProperty("name").GetArrayLength());
        Assert.Equal(1, errors.GetProperty("endDate").GetArrayLength());
    }

    [Fact]
    public async Task LowercasesOnlyTheFirstCharacterOfEachDotSeparatedPathSegment()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var exception = new ValidationException(
            [new ValidationFailure("Owner.DisplayName", "DisplayName is required.")]);

        await HandleAsync(context, exception);

        var body = await ReadBodyAsync(context);
        var errors = body.GetProperty("errors");
        Assert.Equal(1, errors.GetProperty("owner.displayName").GetArrayLength());
    }

    [Fact]
    public async Task LeavesExceptionsItDoesNotOwnToTheFallback()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await HandleAsync(context, new InvalidOperationException());

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    [Fact]
    public void RequiresTheTypeAndTheTitle()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddProblemDetailsMapping(map => map.MapValidation(o => o.Title = "t")));
        Assert.Throws<InvalidOperationException>(() => services.AddProblemDetailsMapping(map => map.MapValidation(o => o.Type = "urn:t")));
    }

    private static async Task<JsonElement> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var document = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: context.RequestAborted);
        return document.RootElement.Clone();
    }
}
