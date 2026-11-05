using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Modules.Organization.Domain;

/// <summary>
/// Organizer-defined grouping of BJCP styles allowed for a competition (frontend wizard step 3).
/// Not to be confused with <c>BjcpStyle.CategoryNumber</c>/<c>BjcpStyle.CategoryName</c>,
/// which is the BJCP taxonomy's own category (e.g. "21"/"IPA") — this is a completely independent,
/// organizer-chosen label used only to group the subset of styles allowed in this competition.
/// Hooking this allow-list into beer-entry validation is a separate, later feature; this entity
/// only persists the grouping.
/// </summary>
internal class CompetitionCategory : Entity
{
    public Guid CompetitionId { get; set; }

    public required string Name { get; set; }

    public int DisplayOrder { get; set; }

    public ICollection<CompetitionCategoryStyle> Styles { get; set; } = [];
}
