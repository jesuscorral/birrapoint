using BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents.Dtos;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BirraPoint.Api.Modules.Judging.Features.Evaluations.Features.SubmitEvaluation;

/// <summary>Descriptors/Feedback (Session 2026-09-21) are optional — a request body that omits them
/// entirely still binds fine (both default to null).</summary>
internal sealed record SubmitEvaluationRequest(
    Guid BeerEntryId, EvaluationScoresDto Scores, EvaluationCommentsDto Comments,
    EvaluationDescriptorsDto? Descriptors = null, string? Feedback = null);

internal static class SubmitEvaluationEndpoint
{
    internal static IEndpointRouteBuilder MapSubmitEvaluationEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/", async (
            Guid tableId,
            [FromHeader(Name = "X-Idempotency-Key")] string? idempotencyKey,
            SubmitEvaluationRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return Results.BadRequest();
            }

            var command = new SubmitEvaluationCommand(
                tableId, request.BeerEntryId, request.Scores, request.Comments, request.Descriptors, request.Feedback);
            var result = await sender.Send(command, cancellationToken);

            return result switch
            {
                null => Results.NotFound(),
                { IsNewSubmission: true } => Results.Created(
                    $"/api/v1/me/tables/{tableId}/evaluations/{result.EvaluationId}", result),
                _ => Results.Ok(result),
            };
        })
        .WithName("SubmitEvaluation")
        .Produces<SubmitEvaluationResult>(StatusCodes.Status201Created)
        .Produces<SubmitEvaluationResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return endpointsGroup;
    }
}
