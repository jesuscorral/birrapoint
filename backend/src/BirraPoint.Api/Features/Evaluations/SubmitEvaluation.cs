using System.Text.Json.Serialization;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.TastingOrder;
using BirraPoint.Api.Realtime;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BirraPoint.Api.Features.Evaluations;

/// <summary>Wire shape for the five capped sections (contracts/rest-api.md §Judge workspace).</summary>
public sealed record EvaluationScoresDto(int Aroma, int Appearance, int Flavor, int Mouthfeel, int Overall);

/// <summary>Wire shape for the five per-section comments (FR-025: each ≥20 chars).</summary>
public sealed record EvaluationCommentsDto(string Aroma, string Appearance, string Flavor, string Mouthfeel, string Overall);

/// <summary>POST /me/tables/{tableId}/evaluations (contracts/rest-api.md §Judge workspace, T055-T058,
/// FR-022/FR-023/FR-025/FR-029). Returns null when the caller is not an active member of this table
/// — the endpoint maps that to a plain 404, same convention as FixOrderCommand/GetTableSamplesQuery.</summary>
public sealed record SubmitEvaluationCommand(
    Guid TableId, Guid BeerEntryId, EvaluationScoresDto Scores, EvaluationCommentsDto Comments)
    : IRequest<SubmitEvaluationResult?>;

/// <summary>contracts/rest-api.md §Judge workspace success shape: `{ evaluationId, status, total,
/// discrepancy? }`. <see cref="Status"/> is "Confirmed" or "PendingConsensus" depending on whether
/// this submission's total lands within 7 points of every other submitted total for the same
/// sample (FR-031). <see cref="IsNewSubmission"/> is not part of the wire contract; the endpoint
/// reads it to choose 201 (fresh insert) vs 200 (idempotent replay, FR-029/R-07) and then it's
/// excluded from the body.</summary>
public sealed record SubmitEvaluationResult(Guid EvaluationId, string Status, int Total, DiscrepancyView? Discrepancy)
{
    [JsonIgnore]
    public bool IsNewSubmission { get; init; }
}

/// <summary>
/// Score caps and comment-length rules only (FR-023/FR-025) — table membership/state/sequence are
/// intentionally NOT validated here (same convention as FixOrderCommandValidator: failing
/// validation for a non-member would leak this table's existence via a 400 instead of the
/// handler's 404; the real precondition gating happens in the handler and throws DomainException).
/// </summary>
public sealed class SubmitEvaluationCommandValidator : AbstractValidator<SubmitEvaluationCommand>
{
    public SubmitEvaluationCommandValidator()
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

public sealed class SubmitEvaluationCommandHandler(AppDbContext dbContext, ICurrentUser currentUser, IEventPublisher eventPublisher)
    : IRequestHandler<SubmitEvaluationCommand, SubmitEvaluationResult?>
{
    public async Task<SubmitEvaluationResult?> Handle(SubmitEvaluationCommand request, CancellationToken cancellationToken)
    {
        var judges = await currentUser.GetJudgeRecordsAsync(cancellationToken);
        var judgeId = await JudgeTableAccess.FindActiveMembershipAsync(dbContext, judges, request.TableId, cancellationToken);
        if (judgeId is null)
        {
            return null;
        }

        // FR-029/R-07 idempotency MUST hold regardless of what happens to the table/competition
        // after the original submission committed: a judge's evaluation is a fact once persisted,
        // and a replay of it (the ack got lost, the outbox retries it later) must always return
        // that stored result — even if the table has since closed or the competition has moved
        // past InEvaluation. Checking this before any precondition gate below is what makes that
        // true; gating first would 409 a legitimate replay simply because time passed since the
        // original successful submit, which is exactly the scenario the outbox/replay engine
        // exists to handle (frontend/src/app/core/offline/sync.service.ts).
        var existingEvaluation = await dbContext.Evaluations
            .SingleOrDefaultAsync(e => e.JudgeId == judgeId && e.BeerEntryId == request.BeerEntryId, cancellationToken);
        if (existingEvaluation is not null)
        {
            // No new reconciliation here — it already ran when this row was first inserted; a
            // replay only needs to reflect whatever discrepancy state resulted from that.
            var replayDiscrepancy = await BuildDiscrepancyViewIfPendingAsync(
                existingEvaluation, request.TableId, judgeId.Value, cancellationToken);
            return new SubmitEvaluationResult(
                existingEvaluation.Id, existingEvaluation.Status.ToString(), existingEvaluation.Total, replayDiscrepancy)
            {
                IsNewSubmission = false,
            };
        }

        // The active TableJudge row above references an existing TastingTable (FK), so this is
        // always found — no need to null-check.
        var table = await dbContext.TastingTables.SingleAsync(t => t.Id == request.TableId, cancellationToken);

        var competitionState = await dbContext.Competitions
            .Where(c => c.Id == table.CompetitionId)
            .Select(c => c.State)
            .SingleAsync(cancellationToken);

        if (!SubmitEvaluationRules.CanSubmitInState(competitionState))
        {
            throw new DomainException(
                DomainErrorType.InvalidStateTransition,
                "Evaluations can only be submitted while the competition is InEvaluation.");
        }

        if (table.OrderFixedByJudgeId is null)
        {
            throw new DomainException(
                DomainErrorType.OrderNotFixed,
                "The tasting order for this table has not been fixed yet.");
        }

        if (table.State != TableState.Open)
        {
            throw new DomainException(
                DomainErrorType.TableClosed,
                "This table is closed; no further evaluations can be submitted.");
        }

        var orderedSampleIds = await dbContext.TableSamples
            .Where(ts => ts.TastingTableId == request.TableId)
            .OrderBy(ts => ts.SequenceOrder)
            .Select(ts => ts.BeerEntryId)
            .ToListAsync(cancellationToken);

        // request.BeerEntryId is guaranteed absent from this judge's already-submitted set here —
        // the early idempotent-replay return above already handled the case where it's present.
        var alreadySubmittedIds = await dbContext.Evaluations
            .Where(e => e.TastingTableId == request.TableId && e.JudgeId == judgeId)
            .Select(e => e.BeerEntryId)
            .ToListAsync(cancellationToken);

        if (!SubmitEvaluationRules.IsNextInSequence(orderedSampleIds, alreadySubmittedIds, request.BeerEntryId))
        {
            throw new DomainException(
                DomainErrorType.OutOfSequence,
                "This sample is not the next one in the fixed tasting order.");
        }

        var evaluation = new Evaluation
        {
            TastingTableId = request.TableId,
            JudgeId = judgeId.Value,
            BeerEntryId = request.BeerEntryId,
            AromaScore = request.Scores.Aroma,
            AppearanceScore = request.Scores.Appearance,
            FlavorScore = request.Scores.Flavor,
            MouthfeelScore = request.Scores.Mouthfeel,
            OverallScore = request.Scores.Overall,
            AromaComment = request.Comments.Aroma,
            AppearanceComment = request.Comments.Appearance,
            FlavorComment = request.Comments.Flavor,
            MouthfeelComment = request.Comments.Mouthfeel,
            OverallComment = request.Comments.Overall,
            // Discrepancy detection (>7-point spread → PendingConsensus + DiscrepancyAlert)
            // activates in US11 — every submission today is simply Confirmed.
            Status = EvaluationStatus.Confirmed,
            SubmittedAt = DateTimeOffset.UtcNow,
        };

        dbContext.Evaluations.Add(evaluation);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // FR-029/R-07 idempotency backstop for a *genuine concurrent* race the early
            // existing-row check above couldn't see (both requests read "no row" before either
            // committed) — the unique index (EvaluationConfiguration) is what actually serializes
            // them. Detach the failed insert — it was never persisted — and return whatever is
            // actually stored; the race is decided by whichever insert Postgres committed first,
            // never assumed to match this request's body.
            dbContext.Entry(evaluation).State = EntityState.Detached;

            var existing = await dbContext.Evaluations
                .SingleAsync(e => e.JudgeId == judgeId && e.BeerEntryId == request.BeerEntryId, cancellationToken);

            // Same "no new reconciliation, just reflect current state" reasoning as the early
            // idempotent-replay path above — whichever insert actually won the race already
            // triggered reconciliation for this (table, entry) pair.
            var raceDiscrepancy = await BuildDiscrepancyViewIfPendingAsync(existing, request.TableId, judgeId.Value, cancellationToken);

            return new SubmitEvaluationResult(existing.Id, existing.Status.ToString(), existing.Total, raceDiscrepancy)
            {
                IsNewSubmission = false,
            };
        }

        // FR-031: compare this submission's total against every other already-submitted total for
        // the same (table, sample); EF identity-maps the reconciler's query back to the `evaluation`
        // instance already tracked above, so evaluation.Status reflects the reconciled state below
        // without needing to re-fetch it.
        var outcome = await DiscrepancyReconciler.ReconcileAsync(dbContext, request.TableId, request.BeerEntryId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var tableProgress = await ComputeTableProgressAsync(request.TableId, cancellationToken);
        var blindCode = await dbContext.BeerEntries
            .Where(e => e.Id == request.BeerEntryId)
            .Select(e => e.BlindCode)
            .SingleAsync(cancellationToken);
        var judgeDisplayName = judges.First(j => j.Id == judgeId).DisplayName;

        // Emitted only after SaveChangesAsync above commits (contracts/signalr-hub.md §Delivery
        // semantics) — never before, to avoid a phantom event for a rolled-back insert.
        await eventPublisher.PublishToOrganizersAsync(
            table.CompetitionId,
            "EvaluationCompleted",
            new { tableId = request.TableId, blindCode, judgeDisplayName, tableProgress },
            CancellationToken.None);

        // Events describe the entry as a whole (any judge involved) — contracts/signalr-hub.md's
        // DiscrepancyRaised/Resolved are entry-level notifications, not judge-scoped.
        if (outcome.InvolvedJudgeIds.Count > 0)
        {
            var payload = new { alertId = outcome.AlertId, tableId = request.TableId, blindCode, involvedJudgeIds = outcome.InvolvedJudgeIds };
            await eventPublisher.PublishToTableAsync(request.TableId, CompetitionEvents.DiscrepancyRaised, payload, CancellationToken.None);
            await eventPublisher.PublishToOrganizersAsync(table.CompetitionId, CompetitionEvents.DiscrepancyRaised, payload, CancellationToken.None);
        }
        else if (outcome.AlertResolved)
        {
            var payload = new { alertId = outcome.AlertId, tableId = request.TableId, blindCode };
            await eventPublisher.PublishToTableAsync(request.TableId, CompetitionEvents.DiscrepancyResolved, payload, CancellationToken.None);
            await eventPublisher.PublishToOrganizersAsync(table.CompetitionId, CompetitionEvents.DiscrepancyResolved, payload, CancellationToken.None);
        }

        // Unlike the events above, the response body's `discrepancy` field reflects the ACTING
        // judge's own situation — mirrors GET .../discrepancies' "involving the caller" semantics.
        // A judge landing cleanly between two already-divergent totals gets Confirmed + null here,
        // even though an alert remains open for this entry.
        var discrepancy = outcome.InvolvedJudgeIds.Contains(judgeId.Value)
            ? await DiscrepancyViewBuilder.BuildAsync(
                dbContext, outcome.AlertId!.Value, request.TableId, request.BeerEntryId, judgeId.Value, cancellationToken)
            : null;

        return new SubmitEvaluationResult(evaluation.Id, evaluation.Status.ToString(), evaluation.Total, discrepancy)
        {
            IsNewSubmission = true,
        };
    }

    /// <summary>Builds the discrepancy view for an idempotent-replay/race-caught early-return path
    /// (no new reconciliation — it already ran when the row was first inserted) by looking up the
    /// Open alert for this (table, entry) when the stored row is currently PendingConsensus.</summary>
    private async Task<DiscrepancyView?> BuildDiscrepancyViewIfPendingAsync(
        Evaluation storedEvaluation, Guid tableId, Guid callerJudgeId, CancellationToken cancellationToken)
    {
        if (storedEvaluation.Status != EvaluationStatus.PendingConsensus)
        {
            return null;
        }

        var alertId = await dbContext.DiscrepancyAlerts
            .Where(a => a.TastingTableId == tableId && a.BeerEntryId == storedEvaluation.BeerEntryId && a.Status == DiscrepancyStatus.Open)
            .Select(a => (Guid?)a.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (alertId is null)
        {
            return null;
        }

        return await DiscrepancyViewBuilder.BuildAsync(
            dbContext, alertId.Value, tableId, storedEvaluation.BeerEntryId, callerJudgeId, cancellationToken);
    }

    private async Task<object> ComputeTableProgressAsync(Guid tableId, CancellationToken cancellationToken)
    {
        var judgeCount = await dbContext.TableJudges
            .CountAsync(tj => tj.TastingTableId == tableId && tj.RemovedAt == null, cancellationToken);
        var sampleCount = await dbContext.TableSamples.CountAsync(ts => ts.TastingTableId == tableId, cancellationToken);
        var expected = judgeCount * sampleCount;
        var completed = await dbContext.Evaluations.CountAsync(e => e.TastingTableId == tableId, cancellationToken);
        var percent = expected == 0 ? 0 : (int)Math.Round(completed * 100.0 / expected, MidpointRounding.AwayFromZero);

        return new { completed, expected, percent };
    }

    /// <summary>
    /// Npgsql wraps a unique-constraint violation in a <see cref="DbUpdateException"/> whose
    /// <see cref="Exception.InnerException"/> is a <see cref="PostgresException"/> with
    /// <c>SqlState == "23505"</c> (<see cref="PostgresErrorCodes.UniqueViolation"/>). No prior slice
    /// in this codebase has needed to detect this — every other unique constraint (e.g. blind code,
    /// participant email) is pre-checked with a query before insert — so this is a new pattern here,
    /// required because locked-on-submit forbids ever pre-checking-then-upserting (R-07): the check
    /// and the insert must be the same atomic operation for the idempotency guarantee to hold under
    /// a genuine race.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
