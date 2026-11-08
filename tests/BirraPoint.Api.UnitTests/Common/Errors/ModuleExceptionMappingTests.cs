using System.Text.Json;
using BirraPoint.Api.Extensions;
using BirraPoint.Api.Modules.Judging;
using BirraPoint.Api.Modules.Judging.Errors;
using BirraPoint.Api.Modules.Organization;
using BirraPoint.Api.Modules.Organization.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.UnitTests.Common.Errors;

/// <summary>
/// Drives the real handler chain built the way Program.cs does: the host's validation mapping plus
/// each module's own exception mapping (ADR-0033). Modules are registered after the host call, so
/// this also proves their mappings win over the block's catch-all 500.
/// </summary>
public sealed class ModuleExceptionMappingTests
{
    private static readonly string[] JudgeIds = ["judge-1"];

    private static async Task<(DefaultHttpContext Context, JsonElement Body)> RunAsync(Exception exception)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetailsErrorHandling();
        services.AddOrganizationModule();
        services.AddJudgingModule();
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
    public async Task JudgingExceptionWritesItsCatalogUrnStatusAndExtensions()
    {
        var exception = new JudgingException(
            JudgingError.ConflictOfInterest,
            extensions: new Dictionary<string, object?> { ["conflicts"] = JudgeIds });

        var (context, body) = await RunAsync(exception);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal("urn:birrapoint:conflict-of-interest", body.GetProperty("type").GetString());
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Equal(JudgingErrorCatalog.Entries[JudgingError.ConflictOfInterest].Title, body.GetProperty("title").GetString());
        Assert.Equal(exception.Message, body.GetProperty("detail").GetString());
        Assert.True(body.TryGetProperty("conflicts", out _));
    }

    [Fact]
    public async Task OrganizationExceptionWritesItsCatalogUrnStatusAndDetail()
    {
        var exception = new OrganizationException(OrganizationError.InvalidImportFile, "The file is empty.");

        var (context, body) = await RunAsync(exception);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("urn:birrapoint:invalid-import-file", body.GetProperty("type").GetString());
        Assert.Equal("Invalid import file", body.GetProperty("title").GetString());
        Assert.Equal("The file is empty.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task BothModulesShareTheInvalidStateTransitionWireContract()
    {
        var (organizationContext, organization) = await RunAsync(new OrganizationException(OrganizationError.InvalidStateTransition));
        var (judgingContext, judging) = await RunAsync(new JudgingException(JudgingError.InvalidStateTransition));

        Assert.Equal(StatusCodes.Status409Conflict, organizationContext.Response.StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, judgingContext.Response.StatusCode);
        Assert.Equal("urn:birrapoint:invalid-state-transition", organization.GetProperty("type").GetString());
        Assert.Equal("urn:birrapoint:invalid-state-transition", judging.GetProperty("type").GetString());
        Assert.Equal("Invalid state transition", organization.GetProperty("title").GetString());
        Assert.Equal("Invalid state transition", judging.GetProperty("title").GetString());
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
