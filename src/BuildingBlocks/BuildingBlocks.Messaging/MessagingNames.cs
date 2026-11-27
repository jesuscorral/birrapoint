namespace BuildingBlocks.Messaging;

/// <summary>Entity-type names of the shared-type outbox and inbox entities. An outbox and an inbox with the same
/// module name must not collide, so each kind has its own prefix.</summary>
internal static class MessagingNames
{
    public static string Outbox(string name) => $"Outbox:{name}";

    public static string Inbox(string name) => $"Inbox:{name}";
}
