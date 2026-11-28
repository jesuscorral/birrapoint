using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Validation.MediatR;

public static class MediatRExtensions
{
    /// <summary>
    /// Registers MediatR handlers, FluentValidation validators, and <see cref="ValidationBehavior{TRequest,TResponse}"/>
    /// as an open pipeline behavior, all discovered from <paramref name="assembly"/>.
    /// </summary>
    public static IServiceCollection AddMediatRWithValidation(this IServiceCollection services, Assembly assembly) =>
        Register(services, [assembly]);

    /// <summary>
    /// Same registration, discovered from every assembly in <paramref name="assemblies"/> (one per module and the host).
    /// The validation behavior is registered once.
    /// </summary>
    public static IServiceCollection AddMediatRWithValidation(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        return Register(services, assemblies);
    }

    private static IServiceCollection Register(IServiceCollection services, Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        }

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssemblies(assemblies);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        return services;
    }
}
