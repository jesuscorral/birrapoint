namespace BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents;

/// <summary>Lightweight catalog projection used for style matching, avoiding a DB dependency in the parser itself.</summary>
internal sealed record StyleCatalogEntry(string Code, string Name);
