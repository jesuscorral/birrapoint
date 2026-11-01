using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Modules.Judging.Features.Tables.Features.UpdateTable;

internal static class UpdateTableEndpoint
{
    internal sealed record UpdateTableRequest(string Name, IReadOnlyList<Guid> JudgeIds, IReadOnlyList<Guid> BeerEntryIds);

    internal static IEndpointRouteBuilder MapUpdateTableEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPut("/{tableId:guid}", async (
            Guid competitionId, Guid tableId, UpdateTableRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new UpdateTableCommand(competitionId, tableId, request.Name, request.JudgeIds, request.BeerEntryIds);
            var result = await sender.Send(command, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
       .WithName("UpdateTable")
       .Produces<TableMutationResult>()
       .Produces(StatusCodes.Status400BadRequest)
       .Produces(StatusCodes.Status404NotFound)
       .Produces(StatusCodes.Status409Conflict);

        return endpointsGroup;
    }
}
