namespace BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents;

/// <summary>One allowed (category, style) pairing under this competition's FR-052 configuration —
/// projected from <see cref="BirraPoint.Api.Modules.Organization.Domain.CompetitionCategoryStyle"/>, used for the
/// FR-053 cross-check. Public (not internal) because it's a parameter type of the public
/// <see cref="BeerWorkbookParser.Parse"/> method, same as <see cref="StyleCatalogEntry"/>/
/// <see cref="CategoryCatalogEntry"/>.</summary>
internal sealed record CategoryStyleCatalogEntry(Guid CategoryId, string StyleCode);
