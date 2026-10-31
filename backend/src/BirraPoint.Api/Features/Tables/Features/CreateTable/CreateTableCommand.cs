using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Tables.CommonComponents;
using BirraPoint.Api.Features.Tables.CommonComponents.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Tables.Features.CreateTable;

internal sealed record CreateTableCommand(Guid CompetitionId, string Name, IReadOnlyList<Guid> JudgeIds, IReadOnlyList<Guid> BeerEntryIds)
    : IRequest<TableMutationResult?>;


internal sealed class CreateTableCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<CreateTableCommand, TableMutationResult?>
{
    public async Task<TableMutationResult?> Handle(CreateTableCommand request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (competition is null)
        {
            return null;
        }

        // Table setup is organizer work only while it can still affect judging (data-model.md
        // §Competition state gates — Draft/Active "setup still editable"), same gate UploadImport
        // already applies to entry import.
        if (competition.State is not (CompetitionState.Draft or CompetitionState.Active))
        {
            throw new DomainException(
                DomainErrorType.InvalidStateTransition,
                "Tables can only be created while the competition is in Draft or Active state.");
        }

        var table = new TastingTable { CompetitionId = competition.Id, Name = request.Name };
        dbContext.TastingTables.Add(table);

        var bosFlaggedEntryIds = await TableAssignmentApplier.ApplyAsync(
            dbContext, table, request.JudgeIds, request.BeerEntryIds, cancellationToken);

        var dto = await TableProjector.ProjectAsync(dbContext, table.Id, cancellationToken);

        return TableMutationResult.From(dto, bosFlaggedEntryIds);
    }
}
