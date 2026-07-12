namespace BirraPoint.Api.Realtime;

/// <summary>
/// The two fixed SignalR group-name formats (contracts/signalr-hub.md). Shared by
/// <see cref="CompetitionHub"/> (joins) and <see cref="EventPublisher"/> (emits) so the format
/// can never drift between the two call sites.
/// </summary>
public static class CompetitionGroups
{
    public static string Organizers(Guid competitionId) => $"competition:{competitionId}:organizers";

    public static string Table(Guid tableId) => $"table:{tableId}";
}
