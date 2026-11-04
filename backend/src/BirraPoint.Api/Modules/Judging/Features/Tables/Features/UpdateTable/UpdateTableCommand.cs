using BirraPoint.Api.Common.Audit;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Catalog.Contracts;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents;
using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents.Dtos;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Realtime;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.Tables.Features.UpdateTable;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record UpdateTableCommand(
    Guid CompetitionId, Guid TableId, string Name, IReadOnlyList<Guid> JudgeIds, IReadOnlyList<Guid> BeerEntryIds)
    : IRequest<TableMutationResult?>;


internal sealed class UpdateTableCommandHandler(
    AppDbContext dbContext, ICurrentUser currentUser, ICompetitionAccess competitionAccess, IAuditWriter auditWriter, IEventPublisher eventPublisher,
    IConflictOfInterestChecker conflictChecker, IStyleCatalog styleCatalog, IJudgeDirectory judgeDirectory,
    IEntryDirectory entryDirectory, IPublisher publisher)
    : IRequestHandler<UpdateTableCommand, TableMutationResult?>
{
    public async Task<TableMutationResult?> Handle(UpdateTableCommand request, CancellationToken cancellationToken)
    {
        var competition = await competitionAccess.GetOwnedAsync(request.CompetitionId, currentUser.Sub, cancellationToken);

        if (competition is null)
        {
            return null;
        }

        // InEvaluation (live judge removal) reads untracked: LiveJudgeRemoval re-reads the rows it
        // mutates under a row lock and must be their first tracked instance (no stale identity map).
        var isInEvaluationState = competition.State == CompetitionState.InEvaluation;
        var tableQuery = dbContext.TastingTables
            .Include(t => t.Judges)
            .Include(t => t.Samples)
            .Where(t => t.Id == request.TableId && t.CompetitionId == competition.Id);
        var table = await (isInEvaluationState ? tableQuery.AsNoTracking() : tableQuery).FirstOrDefaultAsync(cancellationToken);

        if (table is null)
        {
            return null;
        }

        // Checked before anything else, per the contract's PUT semantics: a closed table is
        // terminal regardless of competition state.
        if (table.State == TableState.Closed)
        {
            throw new DomainException(DomainErrorType.TableClosed, "This table is closed and can no longer be edited.");
        }

        // InEvaluation: the only accepted edit is a pure judge removal (FR-039) — see LiveJudgeRemoval.
        if (isInEvaluationState)
        {
            return await LiveJudgeRemoval.ApplyAsync(
                dbContext, styleCatalog, judgeDirectory, entryDirectory, auditWriter, eventPublisher, competition.Id, table.Id,
                request.Name, request.JudgeIds, request.BeerEntryIds, cancellationToken);
        }

        // Otherwise the same state gate as CreateTable (data-model.md §Competition state gates).
        if (competition.State is not (CompetitionState.Draft or CompetitionState.Active))
        {
            throw new DomainException(
                DomainErrorType.InvalidStateTransition,
                "Tables can only be edited while the competition is in Draft or Active state.");
        }

        table.Name = request.Name;

        var bosFlaggedEntryIds = await TableAssignmentApplier.ApplyAsync(
            dbContext, conflictChecker, publisher, table, request.JudgeIds, request.BeerEntryIds, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        var dto = await TableProjector.ProjectAsync(dbContext, styleCatalog, judgeDirectory, entryDirectory, table.Id, cancellationToken);

        return TableMutationResult.From(dto, bosFlaggedEntryIds);
    }
}
