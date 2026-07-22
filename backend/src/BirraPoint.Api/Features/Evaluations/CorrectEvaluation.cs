using BirraPoint.Api.Common.Audit;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Evaluations;

/// <summary>PUT /competitions/{id}/evaluations/{evaluationId} (contracts/rest-api.md §Monitoring &amp;
/// audit, T064-T065, FR-035). Returns null when the evaluation doesn't exist or belongs to a
/// competition not owned by the caller — the endpoint maps that to a plain 404, never leaking
/// existence. Allowed regardless of table state (the whole point is correcting a closed table's
/// evaluation) — organizer-only, no table-state gate here.</summary>
public sealed record CorrectEvaluationCommand(
    Guid CompetitionId, Guid EvaluationId, EvaluationScoresDto Scores, EvaluationCommentsDto Comments)
    : IRequest<CorrectEvaluationResult?>;

public sealed record CorrectEvaluationResult(Guid EvaluationId, int Total, decimal ConsolidatedMean);

/// <summary>
/// Same score caps (FR-023/FR-024) and comment-length rule (FR-025) as
/// SubmitEvaluationCommandValidator, sourced from the same SubmitEvaluationRules constants so the
/// two boundaries can never drift apart — duplicated rather than shared because the two commands
/// have unrelated shapes (this one also carries CompetitionId/EvaluationId).
/// </summary>
public sealed class CorrectEvaluationCommandValidator : AbstractValidator<CorrectEvaluationCommand>
{
    public CorrectEvaluationCommandValidator()
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

public sealed class CorrectEvaluationCommandHandler(AppDbContext dbContext, ICurrentUser currentUser, IAuditWriter auditWriter)
    : IRequestHandler<CorrectEvaluationCommand, CorrectEvaluationResult?>
{
    public async Task<CorrectEvaluationResult?> Handle(CorrectEvaluationCommand request, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);
        if (!competitionExists)
        {
            return null;
        }

        // Scoped through TastingTable.CompetitionId (Evaluation has no CompetitionId of its own) —
        // this also prevents an evaluation id from a different competition being addressed via a
        // route that happens to name a competition the caller does own.
        var evaluation = await dbContext.Evaluations
            .Where(e => e.Id == request.EvaluationId)
            .Join(dbContext.TastingTables, e => e.TastingTableId, t => t.Id, (e, t) => new { Evaluation = e, t.CompetitionId })
            .Where(x => x.CompetitionId == request.CompetitionId)
            .Select(x => x.Evaluation)
            .SingleOrDefaultAsync(cancellationToken);

        if (evaluation is null)
        {
            return null;
        }

        var before = new
        {
            evaluation.AromaScore,
            evaluation.AppearanceScore,
            evaluation.FlavorScore,
            evaluation.MouthfeelScore,
            evaluation.OverallScore,
            evaluation.AromaComment,
            evaluation.AppearanceComment,
            evaluation.FlavorComment,
            evaluation.MouthfeelComment,
            evaluation.OverallComment,
        };

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

        var after = new
        {
            evaluation.AromaScore,
            evaluation.AppearanceScore,
            evaluation.FlavorScore,
            evaluation.MouthfeelScore,
            evaluation.OverallScore,
            evaluation.AromaComment,
            evaluation.AppearanceComment,
            evaluation.FlavorComment,
            evaluation.MouthfeelComment,
            evaluation.OverallComment,
        };

        // Audit stages via the change tracker (does not call SaveChanges) — must run before our own
        // SaveChangesAsync so both commit in the same transaction (mirrors ChangeState.cs/FR-035).
        auditWriter.Record("EvaluationCorrected", nameof(Evaluation), evaluation.Id.ToString(), before, after);

        await dbContext.SaveChangesAsync(cancellationToken);

        // evaluation.Total is a DB-computed generated column; EF/Npgsql refreshes it as part of the
        // SaveChangesAsync above (same pattern SubmitEvaluation.cs relies on) — no manual reload.
        var totals = await dbContext.Evaluations
            .Where(e => e.TastingTableId == evaluation.TastingTableId && e.BeerEntryId == evaluation.BeerEntryId)
            .Select(e => e.Total)
            .ToListAsync(cancellationToken);

        var consolidatedMean = CloseTableRules.ComputeMean(totals);

        return new CorrectEvaluationResult(evaluation.Id, evaluation.Total, consolidatedMean);
    }
}
