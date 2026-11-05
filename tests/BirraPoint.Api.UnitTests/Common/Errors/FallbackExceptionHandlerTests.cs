using System.Text.Json;
using BirraPoint.Api.Common.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace BirraPoint.Api.UnitTests.Common.Errors;

public sealed class FallbackExceptionHandlerTests
{
    private readonly FallbackExceptionHandler _handler =
        new(NullLogger<FallbackExceptionHandler>.Instance, ProblemDetailsServiceTestFactory.Create());

    [Fact]
    public async Task WritesAGeneric500WithoutLeakingExceptionDetails()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var exception = new InvalidOperationException("connection string contains a password=hunter2");

        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType, StringComparison.Ordinal);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var raw = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("hunter2", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), raw, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(raw);
        Assert.Equal(500, document.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task AlwaysHandlesAnyExceptionType()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        var handled = await _handler.TryHandleAsync(context, new InvalidOperationException("anything"), CancellationToken.None);

        Assert.True(handled);
    }
}
