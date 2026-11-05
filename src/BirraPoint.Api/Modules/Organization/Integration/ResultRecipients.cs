using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Integration;

internal sealed class ResultRecipients(AppDbContext dbContext) : IResultRecipients
{
    public async Task<IReadOnlyList<ResultEntry>> GetEntriesAsync(Guid competitionId, CancellationToken cancellationToken = default) =>
        await dbContext.BeerEntries
            .AsNoTracking()
            .Where(e => e.CompetitionId == competitionId)
            .Select(e => new ResultEntry(e.Id, e.ParticipantId, e.BlindCode, e.StyleCode))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ResultEntry>> GetParticipantEntriesAsync(Guid participantId, CancellationToken cancellationToken = default) =>
        await dbContext.BeerEntries
            .AsNoTracking()
            .Where(e => e.ParticipantId == participantId)
            .Select(e => new ResultEntry(e.Id, e.ParticipantId, e.BlindCode, e.StyleCode))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ResultRecipient>> GetRecipientsAsync(Guid competitionId, CancellationToken cancellationToken = default) =>
        await dbContext.Participants
            .AsNoTracking()
            .Where(p => p.CompetitionId == competitionId)
            .Select(p => new ResultRecipient(p.Id, p.Email))
            .ToListAsync(cancellationToken);

    public Task<ResultRecipient?> GetRecipientAsync(Guid participantId, CancellationToken cancellationToken = default) =>
        dbContext.Participants
            .AsNoTracking()
            .Where(p => p.Id == participantId)
            .Select(p => new ResultRecipient(p.Id, p.Email))
            .FirstOrDefaultAsync(cancellationToken);
}
