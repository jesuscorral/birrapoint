using BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Modules.Judging.Features.Evaluations.Features.AdjustEvaluation;

internal sealed record AdjustEvaluationRequest(EvaluationScoresDto Scores, EvaluationCommentsDto Comments);

internal static class AdjustEvaluationEndpoint
{
    internal static IEndpointRouteBuilder MapAdjustEvaluationEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPut("/{evaluationId:guid}", async (
            Guid tableId, Guid evaluationId, AdjustEvaluationRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new AdjustEvaluationCommand(tableId, evaluationId, request.Scores, request.Comments);
            var result = await sender.Send(command, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("AdjustEvaluation")
        .Produces<AdjustEvaluationResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return endpointsGroup;
    }
}
