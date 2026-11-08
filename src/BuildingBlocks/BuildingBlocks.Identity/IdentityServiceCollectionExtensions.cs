using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Identity;

public static class IdentityServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ICurrentUser"/> (scoped) and the <c>IHttpContextAccessor</c> it reads from.</summary>
    public static IServiceCollection AddCurrentUser(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        return services;
    }
}
