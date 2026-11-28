# Data model: One project per module (#141)

No change to the living data model (`specs/001-birrapoint-mvp/data-model.md`): no entity, column, index, constraint,
schema or migration is added, removed or renamed (FR-108).

What changes is only where the code lives and how state is changed:

| Context | Old location | New location (migrations move with it, ids unchanged) |
|---|---|---|
| `JobsDbContext`, `AuditDbContext` | `BirraPoint.Api/Common/Persistence/{Jobs,Audit}` | `BirraPoint.Common/Persistence/{Jobs,Audit}` |
| `CatalogDbContext` | `BirraPoint.Api/Modules/Catalog/Persistence` | `Modules/BirraPoint.Catalog/Persistence` |
| `OrganizationDbContext` | `.../Modules/Organization/Persistence` | `Modules/BirraPoint.Organization/Persistence` |
| `JudgingDbContext` | `.../Modules/Judging/Persistence` | `Modules/BirraPoint.Judging/Persistence` |
| `PublicationDbContext` | `.../Modules/Publication/Persistence` | `Modules/BirraPoint.Publication/Persistence` |

Aggregates whose state-protecting properties become `private set` with behaviour methods (R-141-05): `Competition`,
`Evaluation`, `TastingTable`, `DiscrepancyAlert`. EF maps private setters, so the model snapshot does not change
(`has-pending-model-changes` must report none).
