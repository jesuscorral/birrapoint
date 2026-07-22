using MediatR;

namespace BirraPoint.Api.Features.Monitoring;

/// <summary>Maps GetProgress/GetEntryEvaluations (contracts/rest-api.md §Monitoring &amp; audit,
/// T068-T069) — ORGANIZER-only.</summary>
public static class MonitoringEndpoints
{
    public static IEndpointRouteBuilder MapMonitoringEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}")
            .RequireAuthorization("ORGANIZER")
            .WithTags("Monitoring");

        group.MapGet("/progress", async (Guid competitionId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetProgressQuery(competitionId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetProgress")
        .Produces<IReadOnlyList<TableProgressSummaryDto>>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/entries/{entryId:guid}/evaluations", async (
            Guid competitionId, Guid entryId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetEntryEvaluationsQuery(competitionId, entryId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetEntryEvaluations")
        .Produces<EntryEvaluationsResult>()
        .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
