using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents;
using BirraPoint.Api.Modules.Organization.Contracts;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.Tables.Features.UpdateTable;

internal sealed class UpdateTableCommandValidator : AbstractValidator<UpdateTableCommand>
{
    public UpdateTableCommandValidator(AppDbContext dbContext, ICompetitionAccess competitionAccess, ICurrentUser currentUser)
    {
        var owned = new OwnedCompetitionLookup(competitionAccess, currentUser);

        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);

        RuleFor(c => c.JudgeIds)
            .Must(CommonTableValidationRules.HaveNoDuplicates)
            .WithMessage("Judge ids must not contain duplicates.");

        RuleFor(c => c.BeerEntryIds)
            .Must(CommonTableValidationRules.HaveNoDuplicates)
            .WithMessage("Beer entry ids must not contain duplicates.");

        RuleFor(c => c)
            .MustAsync((command, cancellationToken) =>
                CommonTableValidationRules.IsNameUniqueAsync(dbContext, owned, command.CompetitionId, command.TableId, command.Name, cancellationToken))
            .WithName(nameof(UpdateTableCommand.Name))
            .WithMessage("A table with this name already exists in this competition.")
            .WhenAsync(async (command, ct) =>
                !string.IsNullOrWhiteSpace(command.Name) && await ShouldValidateAssignmentAsync(dbContext, owned, command.CompetitionId, command.TableId, ct));

        RuleFor(c => c)
            .MustAsync((command, cancellationToken) =>
                CommonTableValidationRules.AllJudgesBelongToCompetitionAsync(dbContext, owned, command.CompetitionId, command.JudgeIds, cancellationToken))
            .WithName(nameof(UpdateTableCommand.JudgeIds))
            .WithMessage("One or more judges do not belong to this competition.")
            .WhenAsync((command, ct) => ShouldValidateAssignmentAsync(dbContext, owned, command.CompetitionId, command.TableId, ct));

        RuleFor(c => c)
            .MustAsync((command, cancellationToken) =>
                CommonTableValidationRules.AllEntriesAreAssignableAsync(dbContext, owned, command.CompetitionId, command.TableId, command.BeerEntryIds, cancellationToken))
            .WithName(nameof(UpdateTableCommand.BeerEntryIds))
            .WithMessage("One or more beer entries do not belong to this competition or are already assigned to another table.")
            .WhenAsync((command, ct) => ShouldValidateAssignmentAsync(dbContext, owned, command.CompetitionId, command.TableId, ct));
    }

    internal static async Task<bool> ShouldValidateAssignmentAsync(
        AppDbContext dbContext, OwnedCompetitionLookup owned,
        Guid competitionId, Guid tableId, CancellationToken cancellationToken)
    {
        var competition = await owned.GetOwnedAsync(competitionId, cancellationToken);
        if (competition is null || competition.State == CompetitionState.InEvaluation)
        {
            return false;
        }

        return await dbContext.TastingTables.AnyAsync(
            t => t.Id == tableId && t.CompetitionId == competitionId, cancellationToken);
    }
}
