using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents;
using BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents.Dtos;
using BirraPoint.Api.Modules.Judging.Features.TastingOrder.CommonComponents;
using BirraPoint.Api.Realtime;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.Evaluations.Features.AdjustEvaluation;

/// <summary>PUT /me/tables/{tableId}/evaluations/{evaluationId} (contracts/rest-api.md §Judge
/// workspace, Clarification Q2). Returns null when the caller is not an active member of
/// this table, or when the evaluationId doesn't belong to one of the caller's own submissions at
/// this table — the endpoint maps that to a plain 404, never leaking whether it belongs to someone
/// else (same convention as SubmitEvaluationCommand/CloseTableCommand).</summary>
internal sealed record AdjustEvaluationCommand(
    Guid TableId, Guid EvaluationId, EvaluationScoresDto Scores, EvaluationCommentsDto Comments)
    : IRequest<AdjustEvaluationResult?>;

internal sealed record AdjustEvaluationResult(Guid EvaluationId, string Status, int Total, DiscrepancyView? Discrepancy);

internal sealed class AdjustEvaluationCommandHandler(AppDbContext dbContext, ICurrentUser currentUser, IEventPublisher eventPublisher)
    : IRequestHandler<AdjustEvaluationCommand, AdjustEvaluationResult?>
{
    public async Task<AdjustEvaluationResult?> Handle(AdjustEvaluationCommand request, CancellationToken cancellationToken)
    {
        var judges = await currentUser.GetJudgeRecordsAsync(cancellationToken);
        var judgeId = await JudgeTableAccess.FindActiveMembershipAsync(dbContext, judges, request.TableId, cancellationToken);
        if (judgeId is null)
        {
            return null;
        }

        var evaluation = await dbContext.Evaluations
            .SingleOrDefaultAsync(
                e => e.Id == request.EvaluationId && e.JudgeId == judgeId && e.TastingTableId == request.TableId,
                cancellationToken);
        if (evaluation is null)
        {
            return null;
        }

        // data-model.md: "UPDATE: only while an open DiscrepancyAlert covers this evaluation's
        // (table, entry) and the judge is involved" — re-derive the CURRENT involved set (before
        // applying the edit) rather than trusting DiscrepancyAlert.Status alone, since involvement
        // is judge-specific (spec edge case, ≥3 judges) not just "an alert exists for this sample".
        var currentTotals = await dbContext.Evaluations
            .Where(e => e.TastingTableId == request.TableId && e.BeerEntryId == evaluation.BeerEntryId)
            .ToDictionaryAsync(e => e.JudgeId, e => e.Total, cancellationToken);
        var currentlyInvolved = DiscrepancyRules.ComputeInvolvedJudgeIds(currentTotals);

        if (!currentlyInvolved.Contains(judgeId.Value))
        {
            throw new DomainException(
                DomainErrorType.EvaluationLocked,
                "This evaluation can only be adjusted while an open discrepancy involves this judge.");
        }

        evaluation.AromaScore = request.Scores.Aroma;
        evaluation.AppearanceScore = request.Scores.Appearance;
        evaluation.FlavorScore = request.Scores.Flavor;
        evaluation.MouthfeelScore = request.Scores.Mouthfeel;
        evaluation.OverallScore = request.Scores.Overall;
        evaluation.AromaComment = request.Comments.Aroma;
        evaluation.AppearanceComment = request.Comments.Appearance;
        evaluation.FlavorComment = request.Comments.Flavor;
        evaluation.MouthfeelComment = request.Comments.Mouthfeel;
        evaluation.OverallComment = request.Comments.Overall;

        await dbContext.SaveChangesAsync(cancellationToken);

        var outcome = await DiscrepancyReconciler.ReconcileAndSaveAsync(
            dbContext, request.TableId, evaluation.BeerEntryId, cancellationToken);

        var blindCode = await dbContext.BeerEntries
            .Where(e => e.Id == evaluation.BeerEntryId)
            .Select(e => e.BlindCode)
            .SingleAsync(cancellationToken);

        // Events describe the entry as a whole (any judge involved) — contracts/signalr-hub.md's
        // DiscrepancyRaised/Resolved are entry-level notifications, not judge-scoped.
        if (outcome.InvolvedJudgeIds.Count > 0)
        {
            await PublishDiscrepancyEventAsync(
                eventPublisher, CompetitionEvents.DiscrepancyRaised, request.TableId,
                new { alertId = outcome.AlertId, tableId = request.TableId, blindCode, involvedJudgeIds = outcome.InvolvedJudgeIds },
                cancellationToken);
        }
        else if (outcome.AlertResolved)
        {
            await PublishDiscrepancyEventAsync(
                eventPublisher, CompetitionEvents.DiscrepancyResolved, request.TableId,
                new { alertId = outcome.AlertId, tableId = request.TableId, blindCode },
                cancellationToken);
        }

        // Unlike the events above, the response body's `discrepancy` field reflects the ACTING
        // judge's own situation — mirrors GET .../discrepancies' "involving the caller" semantics.
        // An adjustment can resolve the acting judge's own involvement while other judges at this
        // table remain divergent among themselves; the acting judge's response must not carry a
        // discrepancy they're no longer part of.
        var discrepancy = outcome.InvolvedJudgeIds.Contains(judgeId.Value)
            ? await DiscrepancyViewBuilder.BuildAsync(
                dbContext, outcome.AlertId!.Value, request.TableId, evaluation.BeerEntryId, judgeId.Value, cancellationToken)
            : null;

        return new AdjustEvaluationResult(evaluation.Id, evaluation.Status.ToString(), evaluation.Total, discrepancy);
    }

    // eventPublisher/competitionId resolution shared with GetMyDiscrepanciesQuery's need to look up
    // the table's CompetitionId — see PublishDiscrepancyEventAsync below.
    private async Task PublishDiscrepancyEventAsync(
        IEventPublisher publisher, string eventName, Guid tableId, object payload, CancellationToken cancellationToken)
    {
        var competitionId = await dbContext.TastingTables
            .Where(t => t.Id == tableId)
            .Select(t => t.CompetitionId)
            .SingleAsync(cancellationToken);

        // Emitted only after both SaveChangesAsync calls above commit (contracts/signalr-hub.md
        // §Delivery semantics) — contracts/signalr-hub.md lines 27-28/42 route both events to BOTH
        // the table's judges and the competition's organizers.
        await publisher.PublishToTableAsync(tableId, eventName, payload, CancellationToken.None);
        await publisher.PublishToOrganizersAsync(competitionId, eventName, payload, CancellationToken.None);
    }
}
