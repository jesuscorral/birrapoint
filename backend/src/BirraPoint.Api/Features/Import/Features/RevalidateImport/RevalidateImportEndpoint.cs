using BirraPoint.Api.Features.Import.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Features.Import.Features.RevalidateImport;

internal static class RevalidateImportEndpoint
{
    internal static IEndpointRouteBuilder MapRevalidateImportEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/{importId:guid}/revalidate", async (Guid competitionId, Guid importId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new RevalidateImportCommand(competitionId, importId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("RevalidateImport")
        .Produces<ImportBatchDto>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
