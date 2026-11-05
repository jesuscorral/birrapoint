using BirraPoint.Api.Modules.Judging.Features.TastingOrder.Features.FixOrder;
using BirraPoint.Api.Modules.Judging.Features.TastingOrder.Features.GetMyTables;
using BirraPoint.Api.Modules.Judging.Features.TastingOrder.Features.GetTableJudges;
using BirraPoint.Api.Modules.Judging.Features.TastingOrder.Features.GetTableSamples;

namespace BirraPoint.Api.Modules.Judging.Features.TastingOrder;

/// <summary>Maps GetMyTables/GetTableSamples/FixOrder (contracts/rest-api.md §Judge workspace) — JUDGE-only.</summary>
internal static class BaseTastingOrderEndpoints
{
    public static IEndpointRouteBuilder MapTastingOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/me/tables")
            .RequireAuthorization("JUDGE")
            .WithTags("TastingOrder");

        group.MapGetMyTablesEndpoint()
            .MapGetTableSamplesEndpoint()
            .MapGetTableJudgesEndpoint()
            .MapFixOrderEndpoint();

        return endpoints;
    }
}
