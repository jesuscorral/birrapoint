using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Judges;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record RegisterJudgesCommand(Guid CompetitionId, IReadOnlyList<string> Emails) : IRequest<RegisterJudgesResult?>;

public sealed class RegisterJudgesCommandValidator : AbstractValidator<RegisterJudgesCommand>
{
    public RegisterJudgesCommandValidator()
    {
        RuleFor(c => c.Emails).NotEmpty();
        RuleForEach(c => c.Emails).NotEmpty().EmailAddress();
    }
}

public sealed class RegisterJudgesCommandHandler(
    AppDbContext dbContext, ICurrentUser currentUser, IDispatchJobQueue dispatchJobQueue)
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
                DisplayName = email[..email.IndexOf('@')],
            };
            dbContext.Judges.Add(judge);
            dbContext.Invitations.Add(new Invitation { JudgeId = judge.Id });
            createdJudges.Add(judge);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Enqueued only after the SaveChangesAsync above commits (same convention as every other
        // IDispatchJobQueue/IEventPublisher call — see ChangeState.cs).
        foreach (var judge in createdJudges)
        {
            await dispatchJobQueue.EnqueueAsync(
                competition.Id, DispatchJobType.SendInvitation, new { JudgeId = judge.Id }, cancellationToken);
        }

        return new RegisterJudgesResult(
            createdJudges.Select(j => new CreatedJudgeDto(j.Id, j.Email)).ToList(),
            plan.Skipped);
    }
}
