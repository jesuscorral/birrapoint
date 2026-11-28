using BirraPoint.Api.Modules.Judging.Features.Monitoring.Features.GetEntryEvaluations;
using BirraPoint.Api.Modules.Judging.Features.Monitoring.Features.GetProgress;
using BirraPoint.Common.Security;

namespace BirraPoint.Api.Modules.Judging.Features.Monitoring;

/// <summary>Maps GetProgress/GetEntryEvaluations (contracts/rest-api.md §Monitoring &amp; audit) — ORGANIZER-only.</summary>
internal static class BaseMonitoringEndpoints
{
    public static IEndpointRouteBuilder MapMonitoringEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}")
            .RequireAuthorization(Policies.Organizer)
            .WithTags("Monitoring");

        group.MapGetProgressEndpoint()
            .MapGetEntryEvaluationsEndpoint();

        return endpoints;
    }
}
