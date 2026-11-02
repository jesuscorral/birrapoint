using BirraPoint.Api.Modules.Organization.Features.Competitions;
using BirraPoint.Api.Modules.Organization.Features.Import;
using BirraPoint.Api.Modules.Organization.Features.Judges;

namespace BirraPoint.Api.Modules.Organization;

internal static class OrganizationModuleEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCompetitionsEndpoints();
        endpoints.MapImportEndpoints();
        endpoints.MapJudgesEndpoints();

        return endpoints;
    }
}
