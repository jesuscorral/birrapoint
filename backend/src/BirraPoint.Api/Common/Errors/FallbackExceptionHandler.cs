using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BirraPoint.Api.Common.Errors;

/// <summary>
/// Catches anything the other handlers don't own. Logs the real exception server-side and
/// returns a generic 500 — never the exception message or stack trace (Principle VII: never leak
/// internals to the client).
/// </summary>
public sealed class FallbackExceptionHandler(ILogger<FallbackExceptionHandler> logger, IProblemDetailsService problemDetailsService)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
        };

        httpContext.Response.StatusCode = problemDetails.Status.Value;

        // Exception is passed through for diagnostics (e.g. Development-only enrichment); the
        // default writer never serializes it into the response body, so nothing leaks (Principle VII).
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception,
        });
    }
}
