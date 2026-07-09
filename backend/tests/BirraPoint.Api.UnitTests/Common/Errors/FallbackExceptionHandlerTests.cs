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
    public async Task Writes_a_generic_500_without_leaking_exception_details()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var exception = new InvalidOperationException("connection string contains a password=hunter2");

        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var raw = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("hunter2", raw);
        Assert.DoesNotContain(nameof(InvalidOperationException), raw);

        using var document = JsonDocument.Parse(raw);
        Assert.Equal(500, document.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Always_handles_any_exception_type()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        var handled = await _handler.TryHandleAsync(context, new Exception("anything"), CancellationToken.None);

        Assert.True(handled);
    }
}
