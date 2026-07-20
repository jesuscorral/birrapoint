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

    // Scoped to a competition the caller owns — an unscoped query would let a non-owner learn
    // whether an email is registered in a competition they don't own before the handler's own
    // ownership check ever runs, violating "404 for resources outside the caller's scope, never
    // reveal existence" (rest-api.md convention, senior-code-reviewer PR #18 finding). If the
    // caller doesn't own this competition, this passes trivially — the handler's separate
    // ownership check still returns 404 for the request as a whole. (`Judge` has no `Competition`
    // navigation property, so ownership is checked as its own query rather than a join.)
    private static async Task<bool> IsUniqueWithinOwnedCompetitionAsync(
        AppDbContext dbContext, ICurrentUser currentUser, UpdateJudgeEmailCommand command, CancellationToken cancellationToken)
    {
        var isOwnCompetition = await dbContext.Competitions.AnyAsync(
            c => c.Id == command.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (!isOwnCompetition)
        {
            return true;
        }

        return await dbContext.Judges.AllAsync(
            j => j.Id == command.JudgeId
                || j.CompetitionId != command.CompetitionId
                || j.Email.ToLower() != command.Email.ToLower(),
            cancellationToken);
    }
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
