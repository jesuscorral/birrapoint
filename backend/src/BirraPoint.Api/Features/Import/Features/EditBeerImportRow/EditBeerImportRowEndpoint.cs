using BirraPoint.Api.Features.Import.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Features.Import.Features.EditBeerImportRow;

internal static class EditBeerImportRowEndpoint
{
    internal static IEndpointRouteBuilder MapEditBeerImportRowEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPut("/{importId:guid}/rows/{rowNumber:int}", async (
            Guid competitionId, Guid importId, int rowNumber, EditBeerImportRowRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new EditBeerImportRowCommand(competitionId, importId, rowNumber, request);
            var result = await sender.Send(command, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("EditBeerImportRow")
        .Produces<BeerImportRowDto>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
