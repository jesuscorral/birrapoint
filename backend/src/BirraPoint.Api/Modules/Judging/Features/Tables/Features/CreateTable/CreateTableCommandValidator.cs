using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents;
using BirraPoint.Api.Modules.Organization.Contracts;
using FluentValidation;

namespace BirraPoint.Api.Modules.Judging.Features.Tables.Features.CreateTable;

internal sealed class CreateTableCommandValidator : AbstractValidator<CreateTableCommand>
{
    public CreateTableCommandValidator(AppDbContext dbContext, ICompetitionAccess competitionAccess, ICurrentUser currentUser)
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);

        RuleFor(c => c.JudgeIds)
            .Must(CommonTableValidationRules.HaveNoDuplicates)
            .WithMessage("Judge ids must not contain duplicates.");

        RuleFor(c => c.BeerEntryIds)
            .Must(CommonTableValidationRules.HaveNoDuplicates)
            .WithMessage("Beer entry ids must not contain duplicates.");

        RuleFor(c => c)
            .MustAsync((command, cancellationToken) =>
                CommonTableValidationRules.IsNameUniqueAsync(dbContext, competitionAccess, currentUser, command.CompetitionId, excludeTableId: null, command.Name, cancellationToken))
            .WithName(nameof(CreateTableCommand.Name))
            .WithMessage("A table with this name already exists in this competition.")
            .When(c => !string.IsNullOrWhiteSpace(c.Name));

        RuleFor(c => c)
            .MustAsync((command, cancellationToken) =>
                CommonTableValidationRules.AllJudgesBelongToCompetitionAsync(dbContext, competitionAccess, currentUser, command.CompetitionId, command.JudgeIds, cancellationToken))
            .WithName(nameof(CreateTableCommand.JudgeIds))
            .WithMessage("One or more judges do not belong to this competition.");

        RuleFor(c => c)
            .MustAsync((command, cancellationToken) =>
                CommonTableValidationRules.AllEntriesAreAssignableAsync(dbContext, competitionAccess, currentUser, command.CompetitionId, excludeTableId: null, command.BeerEntryIds, cancellationToken))
            .WithName(nameof(CreateTableCommand.BeerEntryIds))
            .WithMessage("One or more beer entries do not belong to this competition or are already assigned to another table.");
    }
}

