namespace BirraPoint.Contracts.Judging;

/// <summary>Judge seating on tasting tables for other modules and the realtime hub.</summary>
internal interface ITableMembership
{
    /// <summary>True when a judge record of the identity (<paramref name="keycloakUserId"/>, with the
    /// <paramref name="email"/> fallback until the Keycloak id is backfilled) holds an active (not removed) seat on
    /// the table. An unknown table is simply not a membership.</summary>
    Task<bool> IsActiveMemberAsync(
        Guid tableId, string keycloakUserId, string? email, CancellationToken cancellationToken = default);
}
