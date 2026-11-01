using MediatR;

namespace BirraPoint.Api.Features.TastingOrder.Features.GetTableJudges;

internal static class GetTableJudgesEndpoint
{
    internal static IEndpointRouteBuilder MapGetTableJudgesEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/{tableId:guid}/judges", async (Guid tableId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetTableJudgesQuery(tableId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetTableJudges")
        .Produces<IReadOnlyList<JudgeTableMemberDto>>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
