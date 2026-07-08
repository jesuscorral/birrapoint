namespace BirraPoint.Api.Domain;

/// <summary>
/// Creation/modification instants (UTC) carried by every persisted entity (data-model.md).
/// Stamped centrally by <c>AppDbContext.SaveChanges*</c> — never set by handlers.
/// </summary>
public interface ITimestamped
{
    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Base for entities with a Guid v7 (sequential) surrogate key.</summary>
public abstract class Entity : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
