using BirraPoint.Api.Modules.Judging.Features.Tables.Features.CreateTable;
using BirraPoint.Api.Modules.Judging.Features.Tables.Features.ListTables;
using BirraPoint.Api.Modules.Judging.Features.Tables.Features.UpdateTable;
using BirraPoint.Common.Security;

namespace BirraPoint.Api.Modules.Judging.Features.Tables;

internal static class BaseTablesEndpoints
{
    public static IEndpointRouteBuilder MapTablesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}/tables")
            .RequireAuthorization(Policies.Organizer)
            .WithTags("Tables");

        group.MapCreateTableEndpoint()
            .MapUpdateTableEndpoint()
            .MapListTablesEndpoint();

        return endpoints;
    }
}
