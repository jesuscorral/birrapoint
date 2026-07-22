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
/// discrepancy? }`. <see cref="Status"/> is always "Confirmed" for now — discrepancy detection
/// (which can also produce "PendingConsensus") activates in US11, out of this task's scope.
/// <see cref="IsNewSubmission"/> is not part of the wire contract; the endpoint reads it to choose
/// 201 (fresh insert) vs 200 (idempotent replay, FR-029/R-07) and then it's excluded from the body.</summary>
public sealed record SubmitEvaluationResult(Guid EvaluationId, string Status, int Total, object? Discrepancy)
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

        var alreadySubmittedIds = await dbContext.Evaluations
            .Where(e => e.TastingTableId == request.TableId && e.JudgeId == judgeId)
            .Select(e => e.BeerEntryId)
            .ToListAsync(cancellationToken);

        // A replay of a sample this judge already submitted (FR-029/R-07 idempotency) is not a new
        // submission — the sequence gate only applies to samples not yet submitted, otherwise a
        // legitimate retry (or the concurrency loser below) would be rejected as out-of-sequence
        // instead of falling through to the unique-index catch that returns the stored result.
        var isReplayOfAnAlreadySubmittedSample = alreadySubmittedIds.Contains(request.BeerEntryId);

        if (!isReplayOfAnAlreadySubmittedSample
            && !SubmitEvaluationRules.IsNextInSequence(orderedSampleIds, alreadySubmittedIds, request.BeerEntryId))
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
            // FR-029/R-07 idempotency backstop: a concurrent or repeated submission for the same
            // (JudgeId, BeerEntryId) hit the unique index (EvaluationConfiguration) before this one
            // committed. Detach the failed insert — it was never persisted — and return whatever is
            // actually stored, never assuming it matches this request's body (a genuine replay will
            // match; a true race is decided by whichever insert Postgres committed first).
            dbContext.Entry(evaluation).State = EntityState.Detached;

            var existing = await dbContext.Evaluations
                .SingleAsync(e => e.JudgeId == judgeId && e.BeerEntryId == request.BeerEntryId, cancellationToken);

            return new SubmitEvaluationResult(existing.Id, existing.Status.ToString(), existing.Total, Discrepancy: null)
            {
                IsNewSubmission = false,
            };
        }

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

        return new SubmitEvaluationResult(evaluation.Id, evaluation.Status.ToString(), evaluation.Total, Discrepancy: null)
        {
            IsNewSubmission = true,
        };
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
