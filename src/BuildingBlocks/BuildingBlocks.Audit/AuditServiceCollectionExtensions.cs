using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Audit;

public static class AuditServiceCollectionExtensions
{
    /// <summary>
    /// Registers a scoped <see cref="IAuditWriter"/> that stages entries on <typeparamref name="TContext"/>
    /// (which must map the entity with <see cref="AuditModelBuilderExtensions.ApplyAuditLog"/>), the host's
    /// <typeparamref name="TActorAccessor"/>, and <see cref="TimeProvider.System"/> unless the host already
    /// registered a <see cref="TimeProvider"/>.
    /// </summary>
    public static IServiceCollection AddAudit<TContext, TActorAccessor>(this IServiceCollection services)
        where TContext : DbContext
        where TActorAccessor : class, IAuditActorAccessor
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAuditActorAccessor, TActorAccessor>();
        services.AddScoped<IAuditWriter, AuditWriter<TContext>>();
        return services;
    }
}
