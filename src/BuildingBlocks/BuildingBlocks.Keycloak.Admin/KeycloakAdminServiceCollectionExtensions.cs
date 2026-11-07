using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Keycloak.Admin;

public static class KeycloakAdminServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IKeycloakAdminClient"/> as a typed HTTP client. Options come from
    /// <paramref name="section"/> (<c>Authority</c>, <c>AdminClientId</c>, <c>AdminClientSecret</c>),
    /// then <paramref name="configure"/>, and are validated when the host starts.
    /// </summary>
    public static IServiceCollection AddKeycloakAdmin(
        this IServiceCollection services, IConfigurationSection section, Action<KeycloakAdminOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        services.AddHttpClient<IKeycloakAdminClient, KeycloakAdminClient>();
        services.AddSingleton<IValidateOptions<KeycloakAdminOptions>>(provider =>
            new KeycloakAdminOptionsValidator(provider.GetRequiredService<IHostEnvironment>(), section.Path));

        var builder = services.AddOptions<KeycloakAdminOptions>().Bind(section);
        if (configure is not null)
        {
            builder.Configure(configure);
        }

        builder.ValidateOnStart();
        return services;
    }
}
