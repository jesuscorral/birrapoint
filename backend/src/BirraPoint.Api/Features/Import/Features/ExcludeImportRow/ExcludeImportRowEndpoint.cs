using BirraPoint.Api.Features.Import.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Features.Import.Features.ExcludeImportRow;

internal static class ExcludeImportRowEndpoint
{
    internal static IEndpointRouteBuilder MapExcludeImportRowEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/{importId:guid}/rows/{rowNumber:int}/exclude", async (
            Guid competitionId, Guid importId, int rowNumber, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ExcludeImportRowCommand(competitionId, importId, rowNumber), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ExcludeImportRow")
        .Produces<ImportRowDto>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
