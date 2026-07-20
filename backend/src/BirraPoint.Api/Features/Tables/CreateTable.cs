using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Tables;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record CreateTableCommand(Guid CompetitionId, string Name, IReadOnlyList<Guid> JudgeIds, IReadOnlyList<Guid> BeerEntryIds)
    : IRequest<TableMutationResult?>;

public sealed class CreateTableCommandValidator : AbstractValidator<CreateTableCommand>
{
    public CreateTableCommandValidator(AppDbContext dbContext, ICurrentUser currentUser)
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);

        RuleFor(c => c.JudgeIds)
            .Must(TableValidationRules.HaveNoDuplicates)
            .WithMessage("Judge ids must not contain duplicates.");

        RuleFor(c => c.BeerEntryIds)
            .Must(TableValidationRules.HaveNoDuplicates)
            .WithMessage("Beer entry ids must not contain duplicates.");

        RuleFor(c => c)
            .MustAsync((command, cancellationToken) =>
                TableValidationRules.IsNameUniqueAsync(dbContext, currentUser, command.CompetitionId, excludeTableId: null, command.Name, cancellationToken))
            .WithName(nameof(CreateTableCommand.Name))
            .WithMessage("A table with this name already exists in this competition.")
            .When(c => !string.IsNullOrWhiteSpace(c.Name));

        RuleFor(c => c)
            .MustAsync((command, cancellationToken) =>
                TableValidationRules.AllJudgesBelongToCompetitionAsync(dbContext, currentUser, command.CompetitionId, command.JudgeIds, cancellationToken))
            .WithName(nameof(CreateTableCommand.JudgeIds))
            .WithMessage("One or more judges do not belong to this competition.");

        RuleFor(c => c)
            .MustAsync((command, cancellationToken) =>
                TableValidationRules.AllEntriesAreAssignableAsync(dbContext, currentUser, command.CompetitionId, excludeTableId: null, command.BeerEntryIds, cancellationToken))
            .WithName(nameof(CreateTableCommand.BeerEntryIds))
            .WithMessage("One or more beer entries do not belong to this competition or are already assigned to another table.");
    }
}

public sealed class CreateTableCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
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
