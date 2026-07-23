using System.Text.Json;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Dispatch;

/// <summary>Per-recipient result-email delivery status (FR-041, contracts/rest-api.md §Results &amp; dispatch).</summary>
public sealed record DispatchStatusDto(Guid ParticipantId, string Email, string Status, int Attempts, string? LastError);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record GetDispatchStatusQuery(Guid CompetitionId) : IRequest<IReadOnlyList<DispatchStatusDto>?>;

public sealed class GetDispatchStatusQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<GetDispatchStatusQuery, IReadOnlyList<DispatchStatusDto>?>
{
    public async Task<IReadOnlyList<DispatchStatusDto>?> Handle(GetDispatchStatusQuery request, CancellationToken cancellationToken)
    {
        var owns = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);
        if (!owns)
        {
            return null;
        }

        var jobs = await dbContext.DispatchJobs
            .AsNoTracking()
            .Where(j => j.CompetitionId == request.CompetitionId && j.Type == DispatchJobType.SendResultEmail)
            .ToListAsync(cancellationToken);

        var participantEmails = await dbContext.Participants
            .AsNoTracking()
            .Where(p => p.CompetitionId == request.CompetitionId)
            .ToDictionaryAsync(p => p.Id, p => p.Email, cancellationToken);

        return jobs
            .Select(job =>
            {
                var participantId = JsonSerializer.Deserialize<SendResultEmailPayload>(job.PayloadJson)!.ParticipantId;
                return new DispatchStatusDto(
                    participantId,
                    participantEmails.GetValueOrDefault(participantId, string.Empty),
                    job.Status.ToString(),
                    job.Attempts,
                    job.LastError);
            })
            .ToList();
    }
}
