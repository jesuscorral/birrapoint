using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.ProblemDetails;

/// <summary>Writes the first registered mapping that matches the exception; declines otherwise.</summary>
internal sealed class MappedExceptionHandler(ProblemDetailsMappingBuilder mappings, IProblemDetailsService problemDetailsService)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problemDetails = mappings.TryMap(exception);
        if (problemDetails is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        // Writes application/problem+json (RFC 7807) via the registered IProblemDetailsService
        // rather than a plain application/json WriteAsJsonAsync.
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception,
        }).ConfigureAwait(false);
    }
}
