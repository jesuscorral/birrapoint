using BuildingBlocks.ProblemDetails;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BuildingBlocks.Validation.MediatR;

public static class ValidationMappingExtensions
{
    /// <summary>
    /// Maps FluentValidation's <see cref="ValidationException"/> to a 400
    /// <see cref="ValidationProblemDetails"/> whose <c>errors</c> map is keyed by camelCase field
    /// path. Register it before any broader mapping so it wins.
    /// </summary>
    public static ProblemDetailsMappingBuilder MapValidation(
        this ProblemDetailsMappingBuilder builder, Action<ValidationProblemOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ValidationProblemOptions();
        configure(options);
        var type = options.Type ?? throw new InvalidOperationException($"{nameof(ValidationProblemOptions.Type)} is required.");
        var title = options.Title ?? throw new InvalidOperationException($"{nameof(ValidationProblemOptions.Title)} is required.");

        return builder.MapProblem<ValidationException>(exception =>
        {
            // FluentValidation PropertyName is PascalCase ("Name", "EndDate"); every other JSON
            // body is camelCase (System.Text.Json Web defaults), but that naming policy only
            // applies to object properties, never to Dictionary<string,T> keys - so without this
            // conversion, `errors` keys would be the one PascalCase corner of an otherwise
            // all-camelCase API, silently breaking any client-side `errors[fieldName]` lookup.
            var errors = exception.Errors
                .GroupBy(failure => ToCamelCase(failure.PropertyName), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray(), StringComparer.Ordinal);

            return new ValidationProblemDetails(errors)
            {
                Type = type,
                Title = title,
                Status = StatusCodes.Status400BadRequest,
            };
        });
    }

    // Lowercases only the first character of each dot-separated segment (nested property paths,
    // e.g. "Address.Street" -> "address.street"), matching JsonNamingPolicy.CamelCase semantics.
    private static string ToCamelCase(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(segment =>
            segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
}
