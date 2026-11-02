using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.DispatchScoreSheet;

namespace BirraPoint.Api.Modules.Publication;

internal static class PublicationModule
{
    /// <summary>Registers the results dispatch pipeline handlers (GeneratePdfs, BundleZip, SendResultEmail),
    /// each enqueuing the next on success.</summary>
    internal static IServiceCollection AddPublicationModule(this IServiceCollection services)
    {
        services.AddScoped<IDispatchJobHandler, GeneratePdfsHandler>();
        services.AddScoped<IDispatchJobHandler, BundleZipHandler>();
        services.AddScoped<IDispatchJobHandler, SendResultEmailHandler>();
        return services;
    }
}
