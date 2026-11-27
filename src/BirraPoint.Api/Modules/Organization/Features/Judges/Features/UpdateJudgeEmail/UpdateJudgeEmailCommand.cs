using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Contracts.IntegrationEvents;
using BirraPoint.Api.Modules.Organization.Errors;
using BirraPoint.Api.Modules.Organization.Features.Judges.CommonComponents.Dtos;
using BirraPoint.Api.Modules.Organization.Integration;
using BuildingBlocks.Identity;
using BuildingBlocks.Keycloak.Admin;
using BuildingBlocks.Messaging;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Features.Judges.Features.UpdateJudgeEmail;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record UpdateJudgeEmailCommand(Guid CompetitionId, Guid JudgeId, string Email) : IRequest<JudgeProfileDto?>;

internal sealed class UpdateJudgeEmailCommandHandler(
    AppDbContext dbContext, ICurrentUser currentUser, IKeycloakAdminClient keycloakAdminClient,
    IConflictOfInterestChecker conflictChecker, BosFlagRecalculator bosFlagRecalculator, IIntegrationEventPublisher integrationEvents)
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
            throw new OrganizationException(
                OrganizationError.JudgeAlreadyActive,
                "This judge has already logged in; the email can no longer be corrected here.");
        }

        // FR-082: the new address may own entries the old one did not (and the reverse), which changes the
        // Best of Show flags (FR-018). They are recomputed here, in the same transaction as the email change,
        // from the entries owned before and after; the event lets Judging alert the organizers afterwards.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var ownedBefore = await conflictChecker.GetEntriesOwnedByJudgesAsync(competition.Id, [judge.Id], cancellationToken);

        var oldEmail = judge.Email;
        judge.Email = request.Email;
        await dbContext.SaveChangesAsync(cancellationToken);

        var ownedAfter = await conflictChecker.GetEntriesOwnedByJudgesAsync(competition.Id, [judge.Id], cancellationToken);
        var affectedEntryIds = ownedBefore.Values.Concat(ownedAfter.Values).SelectMany(entries => entries).ToHashSet();
        await bosFlagRecalculator.RecalculateAsync(competition.Id, affectedEntryIds, cancellationToken);

        integrationEvents.Publish(new JudgeEmailChanged(competition.Id, judge.Id));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Side effect after the transaction above commits (same convention as
        // IJobQueue/IEventPublisher calls elsewhere — see ChangeCompetitionStateCommand.cs). No-op if the
        // Keycloak account for oldEmail doesn't exist yet: the pending SendInvitation job reads
        // Judge.Email fresh when it eventually runs, so it will provision the corrected address
        // directly instead of needing an update here.
        await keycloakAdminClient.UpdateUserEmailAsync(oldEmail, request.Email, cancellationToken);

        var invitation = await dbContext.Invitations.FirstAsync(i => i.JudgeId == judge.Id, cancellationToken);

        return new JudgeProfileDto(
            judge.Id, judge.Email, judge.DisplayName,
            judge.BjcpRank, judge.BjcpId, judge.PreferredCategory, judge.Preferences,
            invitation.Status, invitation.Attempts, invitation.LastError, invitation.SentAt);
    }
}
