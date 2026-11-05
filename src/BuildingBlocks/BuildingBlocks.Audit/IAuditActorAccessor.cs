namespace BuildingBlocks.Audit;

/// <summary>Implemented by the host: identifies who performs the audited action (e.g. the identity
/// provider subject of the authenticated user, or a system account for background work).</summary>
public interface IAuditActorAccessor
{
    string ActorUserId { get; }
}
