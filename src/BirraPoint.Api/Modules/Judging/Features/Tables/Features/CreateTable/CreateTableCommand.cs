using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Catalog.Contracts;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Judging.Errors;
using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents;
using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents.Dtos;
using BirraPoint.Api.Modules.Organization.Contracts;
using BuildingBlocks.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.Tables.Features.CreateTable;

internal sealed record CreateTableCommand(Guid CompetitionId, string Name, IReadOnlyList<Guid> JudgeIds, IReadOnlyList<Guid> BeerEntryIds)
    : IRequest<TableMutationResult?>;

internal sealed class CreateTableCommandHandler(
    AppDbContext dbContext, ICurrentUser currentUser, ICompetitionAccess competitionAccess, IConflictOfInterestChecker conflictChecker, IStyleCatalog styleCatalog,
    IJudgeDirectory judgeDirectory, IEntryDirectory entryDirectory, IPublisher publisher)
    : IRequestHandler<CreateTableCommand, TableMutationResult?>
{
    /// <summary>Returns null when the competition is not found or not owned by the caller — the
    /// endpoint maps that to a plain 404.</summary>
    public async Task<TableMutationResult?> Handle(CreateTableCommand request, CancellationToken cancellationToken)
    {
        var competition = await FindOwnedCompetitionAsync(request.CompetitionId, cancellationToken);
        if (competition is null)
        {
            return null;
        }

        EnsureTablesCanBeCreated(competition);

        var table = AddTable(competition, request.Name);

        var bosFlaggedEntryIds = await TableAssignmentApplier.ApplyAsync(
            dbContext, conflictChecker, publisher, table, request.JudgeIds, request.BeerEntryIds, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return await BuildResultAsync(table.Id, bosFlaggedEntryIds, cancellationToken);
    }

    private Task<CompetitionSnapshot?> FindOwnedCompetitionAsync(Guid competitionId, CancellationToken cancellationToken) =>
        competitionAccess.GetOwnedAsync(competitionId, currentUser.Sub, cancellationToken);

    /// <summary>Table setup is organizer work only while it can still affect judging (data-model.md
    /// §Competition state gates — Draft/Active "setup still editable"), same gate UploadBeerImport
    /// already applies to entry import.</summary>
    private static void EnsureTablesCanBeCreated(CompetitionSnapshot competition)
    {
        if (competition.State is not (CompetitionState.Draft or CompetitionState.Active))
        {
            throw new JudgingException(
                JudgingError.InvalidStateTransition,
                "Tables can only be created while the competition is in Draft or Active state.");
        }
    }

    private TastingTable AddTable(CompetitionSnapshot competition, string name)
    {
        var table = new TastingTable { CompetitionId = competition.Id, Name = name };
        dbContext.TastingTables.Add(table);
        return table;
    }

    private async Task<TableMutationResult> BuildResultAsync(
        Guid tableId, IReadOnlyList<Guid> bosFlaggedEntryIds, CancellationToken cancellationToken)
    {
        var table = await TableProjector.ProjectAsync(dbContext, styleCatalog, judgeDirectory, entryDirectory, tableId, cancellationToken);
        return TableMutationResult.From(table, bosFlaggedEntryIds);
    }
}
