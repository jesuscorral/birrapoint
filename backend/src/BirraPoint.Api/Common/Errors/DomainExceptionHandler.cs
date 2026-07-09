using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BirraPoint.Api.Common.Errors;

/// <summary>Maps a <see cref="DomainException"/> to its catalogued ProblemDetails shape.</summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
        {
            return false;
        }

        var (urn, status, title) = DomainErrorCatalog.Entries[domainException.ErrorType];

        var problemDetails = new ProblemDetails
        {
            Type = urn,
            Title = title,
            Status = status,
            Detail = domainException.Message,
        };

        foreach (var (key, value) in domainException.Extensions)
        {
            problemDetails.Extensions[key] = value;
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
