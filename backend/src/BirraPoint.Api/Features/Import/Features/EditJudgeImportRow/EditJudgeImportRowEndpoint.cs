using BirraPoint.Api.Features.Import.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Features.Import.Features.EditJudgeImportRow;

internal static class EditJudgeImportRowEndpoint
{
    internal static IEndpointRouteBuilder MapEditJudgeImportRowEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPut("/{importId:guid}/rows/{rowNumber:int}", async (
            Guid competitionId, Guid importId, int rowNumber, EditJudgeImportRowRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new EditJudgeImportRowCommand(competitionId, importId, rowNumber, request);
            var result = await sender.Send(command, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("EditJudgeImportRow")
        .Produces<JudgeImportRowDto>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
