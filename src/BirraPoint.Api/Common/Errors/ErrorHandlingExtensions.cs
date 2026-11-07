using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Validation.MediatR;

namespace BirraPoint.Api.Common.Errors;

internal static class ErrorHandlingExtensions
{
    /// <summary>
    /// Registers ProblemDetails + the cross-cutting validation mapping. Each module adds its own
    /// exception mapping from <c>Add&lt;Module&gt;Module</c> (ADR-0033); the block appends them to the same
    /// table and keeps its catch-all 500 last, so registration order between host and modules is irrelevant.
    /// </summary>
    public static IServiceCollection AddProblemDetailsErrorHandling(this IServiceCollection services) =>
        services.AddProblemDetailsMapping(map =>
            map.MapValidation(options =>
            {
                var (urn, _, title) = HostErrorCatalog.Entries[HostError.Validation];
                options.Type = urn;
                options.Title = title;
            }));
}
