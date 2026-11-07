using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Validation.MediatR;

namespace BirraPoint.Api.Common.Errors;

internal static class ErrorHandlingExtensions
{
    /// <summary>
    /// Registers ProblemDetails + the exception mappings. Mappings are tried in registration order
    /// (domain, then validation) and the block's catch-all 500 stays last, so it only catches what
    /// the more specific mappings didn't.
    /// </summary>
    public static IServiceCollection AddProblemDetailsErrorHandling(this IServiceCollection services) =>
        services.AddProblemDetailsMapping(map =>
        {
            map.Map<DomainException>(ToProblem);
            map.MapValidation(options =>
            {
                var (urn, _, title) = DomainErrorCatalog.Entries[DomainErrorType.Validation];
                options.Type = urn;
                options.Title = title;
            });
        });

    /// <summary>Maps a <see cref="DomainException"/> to its catalogued ProblemDetails shape.</summary>
    internal static ProblemMapping ToProblem(DomainException exception)
    {
        var (urn, status, title) = DomainErrorCatalog.Entries[exception.ErrorType];
        return new ProblemMapping(urn, status, title, exception.Message, exception.Extensions);
    }
}
