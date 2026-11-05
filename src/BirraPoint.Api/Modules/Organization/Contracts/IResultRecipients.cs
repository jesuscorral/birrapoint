namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>Participants and their entries for results delivery (FR-040/FR-041). Only Publication uses it:
/// it runs after tasting ends, when blind anonymity no longer applies.</summary>
internal interface IResultRecipients
{
    /// <summary>Every entry of the competition.</summary>
    Task<IReadOnlyList<ResultEntry>> GetEntriesAsync(Guid competitionId, CancellationToken cancellationToken = default);

    /// <summary>Every entry of one participant.</summary>
    Task<IReadOnlyList<ResultEntry>> GetParticipantEntriesAsync(Guid participantId, CancellationToken cancellationToken = default);

    /// <summary>Every participant of the competition.</summary>
    Task<IReadOnlyList<ResultRecipient>> GetRecipientsAsync(Guid competitionId, CancellationToken cancellationToken = default);

    /// <summary>One participant, or null when unknown.</summary>
    Task<ResultRecipient?> GetRecipientAsync(Guid participantId, CancellationToken cancellationToken = default);
}
