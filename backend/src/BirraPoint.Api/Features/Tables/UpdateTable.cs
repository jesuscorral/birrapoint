using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Tables;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record UpdateTableCommand(
    Guid CompetitionId, Guid TableId, string Name, IReadOnlyList<Guid> JudgeIds, IReadOnlyList<Guid> BeerEntryIds)
    : IRequest<TableMutationResult?>;

public sealed class UpdateTableCommandValidator : AbstractValidator<UpdateTableCommand>
{
    public UpdateTableCommandValidator(AppDbContext dbContext, ICurrentUser currentUser)
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
                TableValidationRules.IsNameUniqueAsync(dbContext, currentUser, command.CompetitionId, command.TableId, command.Name, cancellationToken))
            .WithName(nameof(UpdateTableCommand.Name))
            .WithMessage("A table with this name already exists in this competition.")
            .When(c => !string.IsNullOrWhiteSpace(c.Name));

        RuleFor(c => c)
            .MustAsync((command, cancellationToken) =>
                TableValidationRules.AllJudgesBelongToCompetitionAsync(dbContext, currentUser, command.CompetitionId, command.JudgeIds, cancellationToken))
            .WithName(nameof(UpdateTableCommand.JudgeIds))
            .WithMessage("One or more judges do not belong to this competition.");

        RuleFor(c => c)
            .MustAsync((command, cancellationToken) =>
                TableValidationRules.AllEntriesAreAssignableAsync(dbContext, currentUser, command.CompetitionId, command.TableId, command.BeerEntryIds, cancellationToken))
            .WithName(nameof(UpdateTableCommand.BeerEntryIds))
            .WithMessage("One or more beer entries do not belong to this competition or are already assigned to another table.");
    }
}

public sealed class UpdateTableCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<UpdateTableCommand, TableMutationResult?>
{
    public async Task<TableMutationResult?> Handle(UpdateTableCommand request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (competition is null)
        {
            return null;
        }

        var table = await dbContext.TastingTables
            .Include(t => t.Judges)
            .Include(t => t.Samples)
            .FirstOrDefaultAsync(t => t.Id == request.TableId && t.CompetitionId == competition.Id, cancellationToken);

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

        // Same state gate as CreateTable (data-model.md §Competition state gates). Live judge
        // removal during InEvaluation goes through the separate DELETE endpoint (FR-039), not
        // this full-replace PUT.
        if (competition.State is not (CompetitionState.Draft or CompetitionState.Active))
        {
            throw new DomainException(
                DomainErrorType.InvalidStateTransition,
                "Tables can only be edited while the competition is in Draft or Active state.");
        }

        table.Name = request.Name;

        var bosFlaggedEntryIds = await TableAssignmentApplier.ApplyAsync(
            dbContext, table, request.JudgeIds, request.BeerEntryIds, cancellationToken);

        var dto = await TableProjector.ProjectAsync(dbContext, table.Id, cancellationToken);

        return TableMutationResult.From(dto, bosFlaggedEntryIds);
    }
}
