using BirraPoint.Api.Features.Evaluations.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Features.Evaluations.Features.CorrectEvaluation;

internal sealed record CorrectEvaluationRequest(
    EvaluationScoresDto Scores, EvaluationCommentsDto Comments,
    EvaluationDescriptorsDto? Descriptors = null, string? Feedback = null);

internal static class CorrectEvaluationEndpoint
{
    internal static IEndpointRouteBuilder MapCorrectEvaluationEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPut("/{evaluationId:guid}", async (
            Guid id, Guid evaluationId, CorrectEvaluationRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new CorrectEvaluationCommand(
                id, evaluationId, request.Scores, request.Comments, request.Descriptors, request.Feedback);
            var result = await sender.Send(command, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("CorrectEvaluation")
        .Produces<CorrectEvaluationResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
