using BirraPoint.Api.Features.Import.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Features.Import.Features.EditImportRow;

internal static class EditImportRowEndpoint
{
    internal static IEndpointRouteBuilder MapEditImportRowEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPut("/{importId:guid}/rows/{rowNumber:int}", async (
            Guid competitionId, Guid importId, int rowNumber, EditImportRowRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new EditImportRowCommand(competitionId, importId, rowNumber, request);
            var result = await sender.Send(command, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("EditImportRow")
        .Produces<ImportRowDto>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
