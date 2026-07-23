using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.TastingOrder;
using BirraPoint.Api.Realtime;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Evaluations;

/// <summary>Per-judge total for one discrepancy (contracts/rest-api.md §Judge workspace
/// `GET .../discrepancies`). <see cref="EvaluationId"/> is additive beyond the documented wire
/// shape — the frontend needs it to address the PUT adjustment endpoint.</summary>
public sealed record DiscrepancyTotalDto(string JudgeDisplayName, int Total, bool IsMine, Guid EvaluationId);

/// <summary>contracts/rest-api.md §Judge workspace `{ alertId, blindCode, totals: [...] }`.</summary>
public sealed record DiscrepancyView(Guid AlertId, string BlindCode, IReadOnlyList<DiscrepancyTotalDto> Totals);

/// <summary>
/// Re-derives which judges are involved for one (table, sample) and stages the resulting
/// Evaluation.Status / DiscrepancyAlert open-or-resolve transitions on the change tracker. Does
/// NOT call SaveChangesAsync — the caller's responsibility (same "stage on the change tracker, one
/// save" convention as CorrectEvaluation.cs's audit write), because callers (SubmitEvaluation,
/// AdjustEvaluation) have their own scores mutation to persist in the same round trip.
/// </summary>
internal static class DiscrepancyReconciler
{
    public sealed record DiscrepancyReconciliationOutcome(
        IReadOnlyCollection<Guid> InvolvedJudgeIds, Guid? AlertId, bool AlertOpened, bool AlertResolved);

    public static async Task<DiscrepancyReconciliationOutcome> ReconcileAsync(
        AppDbContext dbContext, Guid tableId, Guid beerEntryId, CancellationToken cancellationToken)
    {
        // Tracked entities (not a projection) — mutating .Status below must stick when the
        // caller's own SaveChangesAsync runs.
        var evaluations = await dbContext.Evaluations
            .Where(e => e.TastingTableId == tableId && e.BeerEntryId == beerEntryId)
            .ToListAsync(cancellationToken);

        var totalsByJudge = evaluations.ToDictionary(e => e.JudgeId, e => e.Total);
        var involved = DiscrepancyRules.ComputeInvolvedJudgeIds(totalsByJudge);

        foreach (var evaluation in evaluations)
        {
            evaluation.Status = involved.Contains(evaluation.JudgeId)
                ? EvaluationStatus.PendingConsensus
                : EvaluationStatus.Confirmed;
        }

        var existingAlert = await dbContext.DiscrepancyAlerts
            .SingleOrDefaultAsync(
                a => a.TastingTableId == tableId && a.BeerEntryId == beerEntryId && a.Status == DiscrepancyStatus.Open,
                cancellationToken);

        if (involved.Count > 0)
        {
            if (existingAlert is not null)
            {
                return new DiscrepancyReconciliationOutcome(involved, existingAlert.Id, AlertOpened: false, AlertResolved: false);
            }

            var alert = new DiscrepancyAlert { TastingTableId = tableId, BeerEntryId = beerEntryId };
            dbContext.DiscrepancyAlerts.Add(alert);
            return new DiscrepancyReconciliationOutcome(involved, alert.Id, AlertOpened: true, AlertResolved: false);
        }

        if (existingAlert is not null)
        {
            existingAlert.Status = DiscrepancyStatus.Resolved;
            existingAlert.ResolvedAt = DateTimeOffset.UtcNow;
            return new DiscrepancyReconciliationOutcome(involved, existingAlert.Id, AlertOpened: false, AlertResolved: true);
        }

        return new DiscrepancyReconciliationOutcome(involved, AlertId: null, AlertOpened: false, AlertResolved: false);
    }
}

/// <summary>Builds the judge-facing view of one alert's totals — used both by the response of the
/// slice that raised/kept it open (SubmitEvaluation, AdjustEvaluation) and by GetMyDiscrepancies.</summary>
internal static class DiscrepancyViewBuilder
{
    public static async Task<DiscrepancyView> BuildAsync(
        AppDbContext dbContext, Guid alertId, Guid tableId, Guid beerEntryId, Guid callerJudgeId, CancellationToken cancellationToken)
    {
        var blindCode = await dbContext.BeerEntries
            .Where(e => e.Id == beerEntryId)
            .Select(e => e.BlindCode)
            .SingleAsync(cancellationToken);

        var totals = await dbContext.Evaluations
            .Where(e => e.TastingTableId == tableId && e.BeerEntryId == beerEntryId)
            .Join(dbContext.Judges, e => e.JudgeId, j => j.Id, (e, j) => new { e.Id, e.Total, e.JudgeId, j.DisplayName })
            .Select(x => new DiscrepancyTotalDto(x.DisplayName, x.Total, x.JudgeId == callerJudgeId, x.Id))
            .ToListAsync(cancellationToken);

        return new DiscrepancyView(alertId, blindCode, totals);
    }
}

/// <summary>PUT /me/tables/{tableId}/evaluations/{evaluationId} (contracts/rest-api.md §Judge
/// workspace, T081, Clarification Q2). Returns null when the caller is not an active member of
/// this table, or when the evaluationId doesn't belong to one of the caller's own submissions at
/// this table — the endpoint maps that to a plain 404, never leaking whether it belongs to someone
/// else (same convention as SubmitEvaluationCommand/CloseTableCommand).</summary>
public sealed record AdjustEvaluationCommand(
    Guid TableId, Guid EvaluationId, EvaluationScoresDto Scores, EvaluationCommentsDto Comments)
    : IRequest<AdjustEvaluationResult?>;

public sealed record AdjustEvaluationResult(Guid EvaluationId, string Status, int Total, DiscrepancyView? Discrepancy);

/// <summary>Same score caps (FR-023/FR-024) and comment-length rule (FR-025) as
/// SubmitEvaluationCommandValidator/CorrectEvaluationCommandValidator, sourced from the same
/// SubmitEvaluationRules constants — duplicated rather than shared because each command has an
/// unrelated shape (this one also carries TableId/EvaluationId).</summary>
public sealed class AdjustEvaluationCommandValidator : AbstractValidator<AdjustEvaluationCommand>
{
    public AdjustEvaluationCommandValidator()
    {
        RuleFor(c => c.Scores).NotNull();
        RuleFor(c => c.Comments).NotNull();

        When(c => c.Scores is not null, () =>
        {
            RuleFor(c => c.Scores.Aroma).InclusiveBetween(0, SubmitEvaluationRules.AromaMax);
            RuleFor(c => c.Scores.Appearance).InclusiveBetween(0, SubmitEvaluationRules.AppearanceMax);
            RuleFor(c => c.Scores.Flavor).InclusiveBetween(0, SubmitEvaluationRules.FlavorMax);
            RuleFor(c => c.Scores.Mouthfeel).InclusiveBetween(0, SubmitEvaluationRules.MouthfeelMax);
            RuleFor(c => c.Scores.Overall).InclusiveBetween(0, SubmitEvaluationRules.OverallMax);
        });

        When(c => c.Comments is not null, () =>
        {
            RuleFor(c => c.Comments.Aroma).NotEmpty().MinimumLength(SubmitEvaluationRules.MinCommentLength);
            RuleFor(c => c.Comments.Appearance).NotEmpty().MinimumLength(SubmitEvaluationRules.MinCommentLength);
            RuleFor(c => c.Comments.Flavor).NotEmpty().MinimumLength(SubmitEvaluationRules.MinCommentLength);
            RuleFor(c => c.Comments.Mouthfeel).NotEmpty().MinimumLength(SubmitEvaluationRules.MinCommentLength);
            RuleFor(c => c.Comments.Overall).NotEmpty().MinimumLength(SubmitEvaluationRules.MinCommentLength);
        });
    }
}

public sealed class AdjustEvaluationCommandHandler(AppDbContext dbContext, ICurrentUser currentUser, IEventPublisher eventPublisher)
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

        var outcome = await DiscrepancyReconciler.ReconcileAsync(
            dbContext, request.TableId, evaluation.BeerEntryId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

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

/// <summary>GET /me/tables/{tableId}/discrepancies (contracts/rest-api.md §Judge workspace, T081).
/// Returns null when the caller is not an active member of this table.</summary>
public sealed record GetMyDiscrepanciesQuery(Guid TableId) : IRequest<IReadOnlyList<DiscrepancyView>?>;

public sealed class GetMyDiscrepanciesQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<GetMyDiscrepanciesQuery, IReadOnlyList<DiscrepancyView>?>
{
    public async Task<IReadOnlyList<DiscrepancyView>?> Handle(GetMyDiscrepanciesQuery request, CancellationToken cancellationToken)
    {
        var judges = await currentUser.GetJudgeRecordsAsync(cancellationToken);
        var judgeId = await JudgeTableAccess.FindActiveMembershipAsync(dbContext, judges, request.TableId, cancellationToken);
        if (judgeId is null)
        {
            return null;
        }

        var openAlerts = await dbContext.DiscrepancyAlerts
            .Where(a => a.TastingTableId == request.TableId && a.Status == DiscrepancyStatus.Open)
            .ToListAsync(cancellationToken);

        var views = new List<DiscrepancyView>();

        foreach (var alert in openAlerts)
        {
            var totals = await dbContext.Evaluations
                .Where(e => e.TastingTableId == request.TableId && e.BeerEntryId == alert.BeerEntryId)
                .ToDictionaryAsync(e => e.JudgeId, e => e.Total, cancellationToken);
            var involved = DiscrepancyRules.ComputeInvolvedJudgeIds(totals);

            if (!involved.Contains(judgeId.Value))
            {
                continue;
            }

            views.Add(await DiscrepancyViewBuilder.BuildAsync(
                dbContext, alert.Id, request.TableId, alert.BeerEntryId, judgeId.Value, cancellationToken));
        }

        return views;
    }
}
