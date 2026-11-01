using BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Import.Features.ExcludeBeerImportRow;

internal static class ExcludeBeerImportRowEndpoint
{
    internal static IEndpointRouteBuilder MapExcludeBeerImportRowEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/{importId:guid}/rows/{rowNumber:int}/exclude", async (
            Guid competitionId, Guid importId, int rowNumber, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ExcludeBeerImportRowCommand(competitionId, importId, rowNumber), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ExcludeBeerImportRow")
        .Produces<BeerImportRowDto>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
