using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Jobs;

public static class JobsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the job queue and its hosted worker over <typeparamref name="TContext"/> (which must map
    /// <see cref="Job"/> with <see cref="JobsModelBuilderExtensions.ApplyJobs"/>). Options bind from
    /// <paramref name="section"/> and are validated on host start. The host registers its
    /// <see cref="IJobHandler"/>s (scoped) and, optionally, <see cref="IJobObserver"/>s.
    /// </summary>
    public static IServiceCollection AddJobs<TContext>(this IServiceCollection services, IConfigurationSection section)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        services.TryAddSingleton<IValidateOptions<JobOptions>, JobOptionsValidator>();
        services.AddOptions<JobOptions>().Bind(section).ValidateOnStart();

        services.TryAddSingleton<JobWakeUp>();
        services.TryAddSingleton<IJobWakeUp>(sp => sp.GetRequiredService<JobWakeUp>());
        services.AddScoped<IJobQueue, JobQueue<TContext>>();
        services.AddHostedService<JobWorker<TContext>>();
        return services;
    }
}
