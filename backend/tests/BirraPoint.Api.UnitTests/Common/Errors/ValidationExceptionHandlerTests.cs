using System.Text.Json;
using BirraPoint.Api.Common.Errors;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;

namespace BirraPoint.Api.UnitTests.Common.Errors;

public sealed class ValidationExceptionHandlerTests
{
    private readonly ValidationExceptionHandler _handler = new(ProblemDetailsServiceTestFactory.Create());

    [Fact]
    public async Task Writes_a_400_with_the_validation_urn_and_a_field_error_map()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var exception = new ValidationException(new[]
        {
            new ValidationFailure("Name", "Name is required."),
            new ValidationFailure("Name", "Name must be under 200 characters."),
            new ValidationFailure("EndDate", "EndDate must be on or after StartDate."),
        });

        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        var body = await ReadBodyAsync(context);
        Assert.Equal("urn:birrapoint:validation", body.GetProperty("type").GetString());
        var errors = body.GetProperty("errors");
        // Keys are camelCase ("name", not "name") even though FluentValidation's PropertyName is
        // PascalCase — every other JSON body in this API is camelCase, and System.Text.Json's
        // naming policy never touches Dictionary<string,T> keys on its own (T025-T030 review).
        Assert.Equal(2, errors.GetProperty("name").GetArrayLength());
        Assert.Equal(1, errors.GetProperty("endDate").GetArrayLength());
    }

    [Fact]
    public async Task Lowercases_only_the_first_character_of_each_dot_separated_path_segment()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var exception = new ValidationException(
            [new ValidationFailure("Owner.DisplayName", "DisplayName is required.")]);

        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        var body = await ReadBodyAsync(context);
        var errors = body.GetProperty("errors");
        Assert.Equal(1, errors.GetProperty("owner.displayName").GetArrayLength());
    }

    [Fact]
    public async Task Returns_false_for_exceptions_it_does_not_own()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        var handled = await _handler.TryHandleAsync(context, new InvalidOperationException(), CancellationToken.None);

        Assert.False(handled);
    }

    private static async Task<JsonElement> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }
}
