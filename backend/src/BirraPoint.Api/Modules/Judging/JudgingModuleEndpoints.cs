using BirraPoint.Api.Modules.Judging.Features.Evaluations;
using BirraPoint.Api.Modules.Judging.Features.Monitoring;
using BirraPoint.Api.Modules.Judging.Features.Tables;
using BirraPoint.Api.Modules.Judging.Features.TastingOrder;

namespace BirraPoint.Api.Modules.Judging;

internal static class JudgingModuleEndpoints
{
    public static IEndpointRouteBuilder MapJudgingModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapTablesEndpoints();
        endpoints.MapTastingOrderEndpoints();
        endpoints.MapEvaluationsEndpoints();
        endpoints.MapMonitoringEndpoints();

        return endpoints;
    }
}
