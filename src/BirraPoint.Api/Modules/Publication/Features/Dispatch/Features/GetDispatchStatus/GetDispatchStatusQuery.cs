using System.Text.Json;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Publication.Features.Dispatch.CommonComponents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.GetDispatchStatus;

/// <summary>Per-recipient result-email delivery status (FR-041, contracts/rest-api.md §Results &amp; dispatch).</summary>
internal sealed record DispatchStatusDto(Guid ParticipantId, string Email, string Status, int Attempts, string? LastError);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record GetDispatchStatusQuery(Guid CompetitionId) : IRequest<IReadOnlyList<DispatchStatusDto>?>;

internal sealed class GetDispatchStatusQueryHandler(
    AppDbContext dbContext, ICurrentUser currentUser, ICompetitionAccess competitionAccess, IResultRecipients resultRecipients)
    : IRequestHandler<GetDispatchStatusQuery, IReadOnlyList<DispatchStatusDto>?>
{
    public async Task<IReadOnlyList<DispatchStatusDto>?> Handle(GetDispatchStatusQuery request, CancellationToken cancellationToken)
    {
        if (await competitionAccess.GetOwnedAsync(request.CompetitionId, currentUser.Sub, cancellationToken) is null)
        {
            return null;
        }

        var jobs = await dbContext.DispatchJobs
            .AsNoTracking()
            .OfDispatchType(DispatchJobType.SendResultEmail)
            .Where(j => j.GroupKey == request.CompetitionId)
            .ToListAsync(cancellationToken);

        var participantEmails = (await resultRecipients.GetRecipientsAsync(request.CompetitionId, cancellationToken))
            .ToDictionary(r => r.ParticipantId, r => r.Email);

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
