using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BirraPoint.Api.Features.Evaluations;

public sealed record SubmitEvaluationRequest(Guid BeerEntryId, EvaluationScoresDto Scores, EvaluationCommentsDto Comments);

public sealed record CorrectEvaluationRequest(EvaluationScoresDto Scores, EvaluationCommentsDto Comments);

/// <summary>Maps POST /me/tables/{tableId}/evaluations (contracts/rest-api.md §Judge workspace,
/// T055-T058), POST /me/tables/{tableId}/close (T062-T063, FR-033/FR-042) — both JUDGE-only — and
/// PUT /competitions/{id}/evaluations/{evaluationId} (T064-T065, FR-035) — ORGANIZER-only.</summary>
public static class EvaluationsEndpoints
{
    public static IEndpointRouteBuilder MapEvaluationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/me/tables/{tableId:guid}/evaluations")
            .RequireAuthorization("JUDGE")
            .WithTags("Evaluations");

        group.MapPost("/", async (
            Guid tableId,
            [FromHeader(Name = "X-Idempotency-Key")] string? idempotencyKey,
            SubmitEvaluationRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            // R-07: the header carries the offline client's deterministic
            // {competitionId}:{tableId}:{judgeId}:{entryId} key; only its presence is a contract
            // requirement here — the actual idempotency guarantee is the server-side
            // (JudgeId, BeerEntryId) unique index (SubmitEvaluation.cs), not this header's content.
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return Results.BadRequest();
            }

            var command = new SubmitEvaluationCommand(tableId, request.BeerEntryId, request.Scores, request.Comments);
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

        // Different path shape than the /evaluations group above — a sibling resource under the
        // same /me/tables/{tableId} prefix, same JUDGE-only convention (mirrors how
        // Features/Tables/TablesEndpoints.cs maps a second, differently-shaped "entries" group from
        // the same file).
        var closeGroup = endpoints.MapGroup("/api/v1/me/tables/{tableId:guid}")
            .RequireAuthorization("JUDGE")
            .WithTags("Evaluations");

        closeGroup.MapPost("/close", async (Guid tableId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new CloseTableCommand(tableId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("CloseTable")
        .Produces<CloseTableResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        var correctionGroup = endpoints.MapGroup("/api/v1/competitions/{id:guid}/evaluations")
            .RequireAuthorization("ORGANIZER")
            .WithTags("Evaluations");

        correctionGroup.MapPut("/{evaluationId:guid}", async (
            Guid id, Guid evaluationId, CorrectEvaluationRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new CorrectEvaluationCommand(id, evaluationId, request.Scores, request.Comments);
            var result = await sender.Send(command, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("CorrectEvaluation")
        .Produces<CorrectEvaluationResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
