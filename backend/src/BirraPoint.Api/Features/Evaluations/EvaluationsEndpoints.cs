using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace BirraPoint.Api.Features.Evaluations;

public sealed record SubmitEvaluationRequest(Guid BeerEntryId, EvaluationScoresDto Scores, EvaluationCommentsDto Comments);

public sealed record AdjustEvaluationRequest(EvaluationScoresDto Scores, EvaluationCommentsDto Comments);

public sealed record CorrectEvaluationRequest(EvaluationScoresDto Scores, EvaluationCommentsDto Comments);

/// <summary>Maps POST /me/tables/{tableId}/evaluations (contracts/rest-api.md §Judge workspace,
/// T055-T058), PUT /me/tables/{tableId}/evaluations/{evaluationId} and
/// GET /me/tables/{tableId}/evaluations/discrepancies (T081, FR-031), POST /me/tables/{tableId}/close
/// (T062-T063, FR-033/FR-042) — all JUDGE-only — and PUT /competitions/{id}/evaluations/{evaluationId}
/// (T064-T065, FR-035) — ORGANIZER-only.</summary>
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

        group.MapPut("/{evaluationId:guid}", async (
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

        // Different path shape than the /evaluations group above — a sibling resource under the
        // same /me/tables/{tableId} prefix, same JUDGE-only convention (mirrors how
        // Features/Tables/TablesEndpoints.cs maps a second, differently-shaped "entries" group from
        // the same file).
        var closeGroup = endpoints.MapGroup("/api/v1/me/tables/{tableId:guid}")
            .RequireAuthorization("JUDGE")
            .WithTags("Evaluations");

        closeGroup.MapPost("/close", async (Guid tableId, ISender sender, CancellationToken cancellationToken) =>
        {
            // CloseTableResult.ConsolidatedScores is organizer-only data (contracts/rest-api.md and
            // signalr-hub.md deliberately withhold per-sample means from judges — they only learn
            // the table closed) — the handler's own return value is used internally to build the
            // organizer SignalR event, but the judge's HTTP response must not include it.
            var result = await sender.Send(new CloseTableCommand(tableId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(new { tableId });
        })
        .WithName("CloseTable")
        .Produces<object>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        // Path matches contracts/rest-api.md §Judge workspace exactly: GET /me/tables/{tableId}/discrepancies
        // (a sibling of /evaluations, not nested under it).
        closeGroup.MapGet("/discrepancies", async (Guid tableId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetMyDiscrepanciesQuery(tableId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetMyDiscrepancies")
        .Produces<IReadOnlyList<DiscrepancyView>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

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
