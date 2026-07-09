using System.Text.Json;
using BirraPoint.Api.Common.Errors;
using Microsoft.AspNetCore.Http;

namespace BirraPoint.Api.UnitTests.Common.Errors;

public sealed class DomainExceptionHandlerTests
{
    private readonly DomainExceptionHandler _handler = new(ProblemDetailsServiceTestFactory.Create());

    [Fact]
    public async Task Writes_the_catalog_urn_status_and_extensions_for_a_domain_exception()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var exception = new DomainException(
            DomainErrorType.ConflictOfInterest,
            extensions: new Dictionary<string, object?> { ["conflicts"] = new[] { "judge-1" } });

        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        var body = await ReadBodyAsync(context);
        Assert.Equal("urn:birrapoint:conflict-of-interest", body.GetProperty("type").GetString());
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.True(body.TryGetProperty("conflicts", out _));
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
