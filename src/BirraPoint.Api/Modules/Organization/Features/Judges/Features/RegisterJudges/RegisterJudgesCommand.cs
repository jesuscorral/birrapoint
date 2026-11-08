using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Organization.Features.Judges.CommonComponents.Dtos;
using BuildingBlocks.Identity;
using BuildingBlocks.Jobs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Features.Judges.Features.RegisterJudges;

internal sealed record CreatedJudgeDto(Guid Id, string Email);

internal sealed record RegisterJudgesResult(IReadOnlyList<CreatedJudgeDto> Created, IReadOnlyList<JudgeSkipDto> Skipped);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record RegisterJudgesCommand(Guid CompetitionId, IReadOnlyList<string> Emails) : IRequest<RegisterJudgesResult?>;

internal sealed class RegisterJudgesCommandHandler(
    AppDbContext dbContext, ICurrentUser currentUser, IJobQueue dispatchJobQueue)
    : IRequestHandler<RegisterJudgesCommand, RegisterJudgesResult?>
{
    public async Task<RegisterJudgesResult?> Handle(RegisterJudgesCommand request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (competition is null)
        {
            return null;
        }

        var existingEmails = new HashSet<string>(
            await dbContext.Judges
                .Where(j => j.CompetitionId == competition.Id)
                .Select(j => j.Email)
                .ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);

        var plan = JudgeRegistrationPlanner.Plan(request.Emails, existingEmails);

        var createdJudges = new List<Judge>();
        foreach (var email in plan.ToCreate)
        {
            var judge = new Judge
            {
                CompetitionId = competition.Id,
                Email = email,
                DisplayName = email[..email.IndexOf('@', StringComparison.Ordinal)],
            };
            dbContext.Judges.Add(judge);
            dbContext.Invitations.Add(new Invitation { JudgeId = judge.Id });
            createdJudges.Add(judge);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Enqueued only after the SaveChangesAsync above commits (same convention as every other
        // IJobQueue/IEventPublisher call — see ChangeCompetitionStateCommand.cs). ProvisionJudgeAccount only
        // (Keycloak account creation, no email) — SendInvitation is now enqueued exclusively by
        // the explicit "Notify judges" action (R-20/Session 2026-08-02, FR-059).
        foreach (var judge in createdJudges)
        {
            await dispatchJobQueue.EnqueueAsync(
                competition.Id, DispatchJobType.ProvisionJudgeAccount, new { JudgeId = judge.Id }, cancellationToken);
        }

        return new RegisterJudgesResult(
            createdJudges.Select(j => new CreatedJudgeDto(j.Id, j.Email)).ToList(),
            plan.Skipped);
    }
}
