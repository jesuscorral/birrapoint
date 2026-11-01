using BirraPoint.Api.Features.Monitoring.Features.GetEntryEvaluations;
using BirraPoint.Api.Features.Monitoring.Features.GetProgress;

namespace BirraPoint.Api.Features.Monitoring;

/// <summary>Maps GetProgress/GetEntryEvaluations (contracts/rest-api.md §Monitoring &amp; audit) — ORGANIZER-only.</summary>
internal static class BaseMonitoringEndpoints
{
    public static IEndpointRouteBuilder MapMonitoringEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}")
            .RequireAuthorization("ORGANIZER")
            .WithTags("Monitoring");

        group.MapGetProgressEndpoint()
            .MapGetEntryEvaluationsEndpoint();

        return endpoints;
    }
}
