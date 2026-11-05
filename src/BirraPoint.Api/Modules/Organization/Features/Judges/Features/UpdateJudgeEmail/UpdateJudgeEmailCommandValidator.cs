using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Features.Judges.Features.UpdateJudgeEmail;

internal sealed class UpdateJudgeEmailCommandValidator : AbstractValidator<UpdateJudgeEmailCommand>
{
    public UpdateJudgeEmailCommandValidator(AppDbContext dbContext, ICurrentUser currentUser)
    {
        RuleFor(c => c.Email)
            .NotEmpty()
            .EmailAddress()
            .DependentRules(() =>
                RuleFor(c => c)
                    .MustAsync((command, cancellationToken) => IsUniqueWithinOwnedCompetitionAsync(dbContext, currentUser, command, cancellationToken))
                    .WithName(nameof(UpdateJudgeEmailCommand.Email))
                    .WithMessage("This email is already registered to another judge in this competition."));
    }

    private static async Task<bool> IsUniqueWithinOwnedCompetitionAsync(
        AppDbContext dbContext, ICurrentUser currentUser, UpdateJudgeEmailCommand command, CancellationToken cancellationToken)
    {
        var isOwnCompetition = await dbContext.Competitions.AnyAsync(
            c => c.Id == command.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (!isOwnCompetition)
        {
            return true;
        }

        // Case-insensitive, evaluated in PostgreSQL (see LikePattern).
        var emailPattern = LikePattern.Escape(command.Email);
        return await dbContext.Judges.AllAsync(
            j => j.Id == command.JudgeId
                || j.CompetitionId != command.CompetitionId
                || !EF.Functions.ILike(j.Email, emailPattern, LikePattern.EscapeCharacter),
            cancellationToken);
    }
}
