using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Keycloak;
using BirraPoint.Api.Common.Persistence;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Judges;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record UpdateJudgeEmailCommand(Guid CompetitionId, Guid JudgeId, string Email) : IRequest<JudgeProfileDto?>;

public sealed class UpdateJudgeEmailCommandValidator : AbstractValidator<UpdateJudgeEmailCommand>
{
    public UpdateJudgeEmailCommandValidator(AppDbContext dbContext)
    {
        RuleFor(c => c.Email)
            .NotEmpty()
            .EmailAddress()
            .DependentRules(() =>
                RuleFor(c => c)
                    .MustAsync((command, cancellationToken) => IsUniqueWithinCompetitionAsync(dbContext, command, cancellationToken))
                    .WithName(nameof(UpdateJudgeEmailCommand.Email))
                    .WithMessage("This email is already registered to another judge in this competition."));
    }

    private static Task<bool> IsUniqueWithinCompetitionAsync(
        AppDbContext dbContext, UpdateJudgeEmailCommand command, CancellationToken cancellationToken) =>
        dbContext.Judges.AllAsync(
            j => j.Id == command.JudgeId
                || j.CompetitionId != command.CompetitionId
                || j.Email.ToLower() != command.Email.ToLower(),
            cancellationToken);
}

public sealed class UpdateJudgeEmailCommandHandler(
    AppDbContext dbContext, ICurrentUser currentUser, IKeycloakAdminClient keycloakAdminClient)
    : IRequestHandler<UpdateJudgeEmailCommand, JudgeProfileDto?>
{
    public async Task<JudgeProfileDto?> Handle(UpdateJudgeEmailCommand request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (competition is null)
        {
            return null;
        }

        var judge = await dbContext.Judges
            .FirstOrDefaultAsync(j => j.Id == request.JudgeId && j.CompetitionId == competition.Id, cancellationToken);

        if (judge is null)
        {
            return null;
        }

        if (judge.KeycloakUserId is not null)
        {
            throw new DomainException(
                DomainErrorType.JudgeAlreadyActive,
                "This judge has already logged in; the email can no longer be corrected here.");
        }

        // Scope cut (approved): COI/BOS re-check against BeerEntry/Participant/EntryCollaborator
        // emails (FR-017/FR-018) is deferred until Features/Tables exists (Phase 7) — the contract
        // calls for it, but there is nothing to re-run it against yet.
        var oldEmail = judge.Email;
        judge.Email = request.Email;

        await dbContext.SaveChangesAsync(cancellationToken);

        // Side effect after the transaction above commits (same convention as
        // IDispatchJobQueue/IEventPublisher calls elsewhere — see ChangeState.cs). No-op if the
        // Keycloak account for oldEmail doesn't exist yet: the pending SendInvitation job reads
        // Judge.Email fresh when it eventually runs, so it will provision the corrected address
        // directly instead of needing an update here.
        await keycloakAdminClient.UpdateUserEmailAsync(oldEmail, request.Email, cancellationToken);

        var invitation = await dbContext.Invitations.FirstAsync(i => i.JudgeId == judge.Id, cancellationToken);

        return new JudgeProfileDto(
            judge.Id, judge.Email, judge.DisplayName, invitation.Status, invitation.Attempts, invitation.LastError, invitation.SentAt);
    }
}
