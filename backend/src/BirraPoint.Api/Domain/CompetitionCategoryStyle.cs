namespace BirraPoint.Api.Domain;

/// <summary>
/// Join row assigning one <see cref="BjcpStyle"/> to one organizer-defined
/// <see cref="CompetitionCategory"/>. <see cref="CompetitionId"/> is a denormalized copy of the
/// owning category's competition id — needed only so a DB-level unique index can enforce "a given
/// BJCP style may belong to at most one category within the same competition" without a
/// cross-table subquery constraint.
/// </summary>
public class CompetitionCategoryStyle : ITimestamped
{
    public Guid CompetitionCategoryId { get; set; }

    public required string StyleCode { get; set; }

    public Guid CompetitionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
