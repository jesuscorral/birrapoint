using System.Text.Json;
using BirraPoint.Api.Common.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.UnitTests.Common.Errors;

/// <summary>Drives the real handler chain registered by <c>AddProblemDetailsErrorHandling</c>.</summary>
public sealed class DomainExceptionMappingTests
{
    private static readonly string[] JudgeIds = ["judge-1"];

    private static async Task<(DefaultHttpContext Context, JsonElement Body)> RunAsync(Exception exception)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetailsErrorHandling();
        using var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider, Response = { Body = new MemoryStream() } };
        foreach (var handler in provider.GetServices<IExceptionHandler>())
        {
            if (await handler.TryHandleAsync(context, exception, CancellationToken.None))
            {
                break;
            }
        }

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var document = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: context.RequestAborted);
        return (context, document.RootElement.Clone());
    }

    [Fact]
    public async Task WritesTheCatalogUrnStatusAndExtensionsForADomainException()
    {
        var exception = new DomainException(
            DomainErrorType.ConflictOfInterest,
            extensions: new Dictionary<string, object?> { ["conflicts"] = JudgeIds });

        var (context, body) = await RunAsync(exception);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal("urn:birrapoint:conflict-of-interest", body.GetProperty("type").GetString());
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Equal(DomainErrorCatalog.Entries[DomainErrorType.ConflictOfInterest].Title, body.GetProperty("title").GetString());
        Assert.Equal(exception.Message, body.GetProperty("detail").GetString());
        Assert.True(body.TryGetProperty("conflicts", out _));
    }

    [Fact]
    public async Task WritesTheValidationUrnForAValidationException()
    {
        var exception = new FluentValidation.ValidationException([new FluentValidation.Results.ValidationFailure("Name", "Name is required.")]);

        var (context, body) = await RunAsync(exception);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("urn:birrapoint:validation", body.GetProperty("type").GetString());
        Assert.Equal(1, body.GetProperty("errors").GetProperty("name").GetArrayLength());
    }

    [Fact]
    public async Task FallsToAGeneric500ForExceptionsNoMappingOwns()
    {
        var (context, body) = await RunAsync(new InvalidOperationException("secret"));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.DoesNotContain("secret", body.GetRawText(), StringComparison.Ordinal);
    }
}
