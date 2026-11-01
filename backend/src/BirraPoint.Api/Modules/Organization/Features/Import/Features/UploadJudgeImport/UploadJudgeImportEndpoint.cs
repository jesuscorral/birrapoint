using BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Import.Features.UploadJudgeImport;

internal static class UploadJudgeImportEndpoint
{
    internal static IEndpointRouteBuilder MapUploadJudgeImportEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/", async (Guid competitionId, IFormFile file, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new UploadJudgeImportCommand(competitionId, file), cancellationToken);
            return result is null
                ? Results.NotFound()
                : Results.Created($"/api/v1/competitions/{competitionId}/imports/judges/{result.ImportId}", result);
        })
        .WithName("UploadJudgeImport")
        .DisableAntiforgery()
        .Produces<JudgeImportBatchDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
