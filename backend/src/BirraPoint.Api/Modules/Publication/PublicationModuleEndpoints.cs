using BirraPoint.Api.Modules.Publication.Features.Dispatch;

namespace BirraPoint.Api.Modules.Publication;

internal static class PublicationModuleEndpoints
{
    public static IEndpointRouteBuilder MapPublicationModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDispatchEndpoints();

        return endpoints;
    }
}
