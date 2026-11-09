using BirraPoint.Api.Modules.Judging.Contracts.IntegrationEvents;
using BirraPoint.Api.Modules.Organization.Contracts.IntegrationEvents;
using BirraPoint.Api.Modules.Publication.Features.Dispatch.CommonComponents;
using BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.DispatchScoreSheet;
using BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.RefreshResults;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging;

namespace BirraPoint.Api.Modules.Publication;

internal static class PublicationModule
{
    /// <summary>Name of Publication's inbox (the schema-qualified table is mapped in the host DbContext).</summary>
    internal const string Messaging = "Publication";

    /// <summary>Registers the results dispatch pipeline handlers (GeneratePdfs, BundleZip, SendResultEmail),
    /// each enqueuing the next on success, the results refresh after a correction, and the module's
    /// integration event subscriptions.</summary>
    internal static IServiceCollection AddPublicationModule(this IServiceCollection services)
    {
        services.AddScoped<ResultsArchiveBuilder>();
        services.AddScoped<IJobHandler, GeneratePdfsHandler>();
        services.AddScoped<IJobHandler, BundleZipHandler>();
        services.AddScoped<IJobHandler, SendResultEmailHandler>();
        services.AddScoped<IJobHandler, RefreshResultsHandler>();

        services.ConfigureMessaging(messaging => messaging
            .Subscribe<CompetitionFinalized, StageGeneratePdfsOnCompetitionFinalized>(Messaging)
            .Subscribe<EvaluationCorrected, StageRefreshOnEvaluationCorrected>(Messaging));
        return services;
    }
}
