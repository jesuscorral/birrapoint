using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.Common.Behaviors;

public static class MediatRExtensions
{
    /// <summary>
    /// Registers MediatR handlers, FluentValidation validators, and <see cref="ValidationBehavior{TRequest,TResponse}"/>
    /// as an open pipeline behavior (T013), all discovered from <paramref name="assembly"/>.
    /// </summary>
    public static IServiceCollection AddMediatRWithValidation(this IServiceCollection services, Assembly assembly)
    {
        services.AddValidatorsFromAssembly(assembly);
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        return services;
    }
}
