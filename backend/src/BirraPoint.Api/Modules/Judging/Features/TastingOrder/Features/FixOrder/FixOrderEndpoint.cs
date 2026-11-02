using BirraPoint.Api.Modules.Judging.Features.TastingOrder.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Modules.Judging.Features.TastingOrder.Features.FixOrder;

internal static class FixOrderEndpoint
{
    internal static IEndpointRouteBuilder MapFixOrderEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/{tableId:guid}/order", async (
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

        return endpointsGroup;
    }
}
