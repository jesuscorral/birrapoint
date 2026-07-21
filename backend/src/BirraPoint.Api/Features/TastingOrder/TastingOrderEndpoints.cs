using MediatR;

namespace BirraPoint.Api.Features.TastingOrder;

public sealed record FixOrderRequest(IReadOnlyList<Guid> OrderedBeerEntryIds);

/// <summary>Maps GetMyTables/GetTableSamples/FixOrder (contracts/rest-api.md §Judge workspace,
/// T050-T052) — JUDGE-only.</summary>
public static class TastingOrderEndpoints
{
    public static IEndpointRouteBuilder MapTastingOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/me/tables")
            .RequireAuthorization("JUDGE")
            .WithTags("TastingOrder");

        group.MapGet("/", async (ISender sender, CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new GetMyTablesQuery(), cancellationToken)))
        .WithName("GetMyTables")
        .Produces<IReadOnlyList<JudgeTableSummaryDto>>();

        group.MapGet("/{tableId:guid}/samples", async (Guid tableId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetTableSamplesQuery(tableId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetTableSamples")
        .Produces<IReadOnlyList<JudgeSampleDto>>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{tableId:guid}/order", async (
            Guid tableId, FixOrderRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new FixOrderCommand(tableId, request.OrderedBeerEntryIds);
            var result = await sender.Send(command, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("FixTastingOrder")
        .Produces<IReadOnlyList<JudgeSampleDto>>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return endpoints;
    }
}
