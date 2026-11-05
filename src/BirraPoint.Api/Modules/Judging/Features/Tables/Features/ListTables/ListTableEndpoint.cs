using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Modules.Judging.Features.Tables.Features.ListTables;

internal static class ListTableEndpoint
{
    internal static IEndpointRouteBuilder MapListTablesEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/", async (Guid competitionId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ListTablesQuery(competitionId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ListTables")
        .Produces<IReadOnlyList<TableDto>>()
        .Produces(StatusCodes.Status404NotFound);
        return endpointsGroup;
    }
}
