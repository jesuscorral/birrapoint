using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BirraPoint.Api.Common.Errors;

/// <summary>Maps a FluentValidation <see cref="ValidationException"/> to a 400 with a per-field error map.</summary>
public sealed class ValidationExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ValidationException validationException)
        {
            return false;
        }

        var (urn, status, title) = DomainErrorCatalog.Entries[DomainErrorType.Validation];

        // FluentValidation's PropertyName is PascalCase ("Name", "EndDate"); every other JSON
        // body in this API is camelCase (System.Text.Json's Web defaults), but that naming policy
        // only applies to object properties, never to Dictionary<string,T> keys — so without this
        // conversion, `errors` keys would be the one PascalCase corner of an otherwise all-camelCase
        // API, silently breaking any client-side `errors[fieldName]` lookup (senior-code-reviewer,
        // T025-T030 review: caught because this handler had no real consumer before Competitions).
        var errors = validationException.Errors
            .GroupBy(failure => ToCamelCase(failure.PropertyName))
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray());

        var problemDetails = new ValidationProblemDetails(errors)
        {
            Type = urn,
            Title = title,
            Status = status,
        };

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = validationException,
        });
    }

    // Lowercases only the first character of each dot-separated segment (nested property paths,
    // e.g. "Address.Street" -> "address.street"), matching JsonNamingPolicy.CamelCase semantics.
    private static string ToCamelCase(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(segment =>
            segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
}
