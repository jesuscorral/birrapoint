using MediatR;

namespace BirraPoint.Api.Features.Evaluations.Features.CloseTable;

internal static class CloseTableEndpoint
{
    internal static IEndpointRouteBuilder MapCloseTableEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/close", async (Guid tableId, ISender sender, CancellationToken cancellationToken) =>
        {
            // CloseTableResult.ConsolidatedScores is organizer-only data (contracts/rest-api.md and
            // signalr-hub.md deliberately withhold per-sample means from judges — they only learn
            // the table closed) — the handler's own return value is used internally to build the
            // organizer SignalR event, but the judge's HTTP response must not include it.
            var result = await sender.Send(new CloseTableCommand(tableId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(new { tableId });
        })
        .WithName("CloseTable")
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return endpointsGroup;
    }
}
