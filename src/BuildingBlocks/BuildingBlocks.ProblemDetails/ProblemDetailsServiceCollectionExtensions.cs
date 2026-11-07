using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.ProblemDetails;

public static class ProblemDetailsServiceCollectionExtensions
{
    /// <summary>
    /// Registers ProblemDetails plus the exception handler chain: one handler that applies the
    /// mappings declared here in registration order, then a catch-all 500 that logs the real
    /// exception server-side and never leaks its message or stack. Calling this more than once
    /// appends mappings to the same table and keeps the catch-all registered last, once.
    /// </summary>
    public static IServiceCollection AddProblemDetailsMapping(
        this IServiceCollection services, Action<ProblemDetailsMappingBuilder> map)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(map);

        var existing = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ProblemDetailsMappingBuilder>()
            .FirstOrDefault();

        if (existing is not null)
        {
            map(existing);
            return services;
        }

        var builder = new ProblemDetailsMappingBuilder();
        map(builder);

        services.AddProblemDetails();
        services.AddSingleton(builder);
        services.AddExceptionHandler<MappedExceptionHandler>();
        services.AddExceptionHandler<FallbackExceptionHandler>();
        return services;
    }
}
