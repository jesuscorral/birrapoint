namespace BuildingBlocks.Audit;

/// <summary>
/// One immutable audit trail entry. <see cref="DataJson"/> holds the before/after payload; hosts decide
/// what goes in it and must keep credentials and unnecessary personal data out.
/// </summary>
public sealed class AuditLog
{
    public const int ActorUserIdMaxLength = 255;
    public const int ActionMaxLength = 100;
    public const int EntityTypeMaxLength = 100;
    public const int EntityIdMaxLength = 50;

    /// <summary>Sequential (v7) Guid, so inserts stay index-friendly.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Who did it, as identified by the host (<see cref="IAuditActorAccessor"/>).</summary>
    public required string ActorUserId { get; set; }

    public required string Action { get; set; }

    public required string EntityType { get; set; }

    public required string EntityId { get; set; }

    /// <summary>JSON object <c>{ "before": …, "after": … }</c>.</summary>
    public required string DataJson { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
