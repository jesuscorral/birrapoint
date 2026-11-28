# Data model: One data context per module

The living model is amended in place: [specs/001-birrapoint-mvp/data-model.md](../001-birrapoint-mvp/data-model.md)
(§Schemas and every cross-module reference row). Tables, columns, indexes and unique constraints are
unchanged; only cross-schema foreign keys and the migration history location change.

## Context map

| Context | Schema | Tables | History |
|---|---|---|---|
| `JobsDbContext` | `jobs` | `DispatchJobs` | `jobs.__EFMigrationsHistory` |
| `AuditDbContext` | `audit` | `AuditLogs` | `audit.__EFMigrationsHistory` |
| `CatalogDbContext` | `catalog` | `BjcpStyles` (seeded) | `catalog.__EFMigrationsHistory` |
| `OrganizationDbContext` | `organization` | module tables + `OutboxMessages` + `InboxMessages` | `organization.__EFMigrationsHistory` |
| `JudgingDbContext` | `judging` | module tables + `OutboxMessages` + `InboxMessages` | `judging.__EFMigrationsHistory` |
| `PublicationDbContext` | `publication` | module tables + `InboxMessages` | `publication.__EFMigrationsHistory` |

Startup migrates in the order of the table (FR-097).

## Cross-module references (ids only, FR-093)

The 12 foreign keys listed in [research.md R-107-0](./research.md) are removed. Same-schema foreign
keys (e.g. `judging.Evaluations → judging.TastingTables`, `organization.BeerEntries →
organization.Participants`) stay.
