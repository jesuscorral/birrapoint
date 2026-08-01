namespace BirraPoint.Api.Domain;

/// <summary>
/// Organizer-defined grouping of BJCP styles allowed for a competition (frontend wizard step 3).
/// Not to be confused with <see cref="BjcpStyle.CategoryNumber"/>/<see cref="BjcpStyle.CategoryName"/>,
/// which is the BJCP taxonomy's own category (e.g. "21"/"IPA") — this is a completely independent,
/// organizer-chosen label used only to group the subset of styles allowed in this competition.
/// Hooking this allow-list into beer-entry validation is a separate, later feature; this entity
/// only persists the grouping.
/// </summary>
public class CompetitionCategory : Entity
{
    public Guid CompetitionId { get; set; }

    public required string Name { get; set; }

    public int DisplayOrder { get; set; }

    public ICollection<CompetitionCategoryStyle> Styles { get; set; } = [];
}
