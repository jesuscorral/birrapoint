using BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.DispatchScoreSheet;
using BuildingBlocks.Jobs;

namespace BirraPoint.Api.Modules.Publication;

internal static class PublicationModule
{
    /// <summary>Registers the results dispatch pipeline handlers (GeneratePdfs, BundleZip, SendResultEmail),
    /// each enqueuing the next on success.</summary>
    internal static IServiceCollection AddPublicationModule(this IServiceCollection services)
    {
        services.AddScoped<IJobHandler, GeneratePdfsHandler>();
        services.AddScoped<IJobHandler, BundleZipHandler>();
        services.AddScoped<IJobHandler, SendResultEmailHandler>();
        return services;
    }
}
