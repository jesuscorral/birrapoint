# Data Model: Database reset, single baseline migration, one schema per module

No entity, column, key, index or constraint changes (FR-067). This feature only assigns each table to
a schema. The baseline data model stays in `specs/001-birrapoint-mvp/data-model.md`, which gets a
**Schema** column (or a per-section schema note) from this table.

| Schema | Owner | Tables |
|---|---|---|
| `catalog` | Catalog module | `BjcpStyles` |
| `organization` | Organization module | `Competitions`, `Organizers`, `CompetitionCategories`, `CompetitionCategoryStyles`, `Participants`, `BeerEntries`, `EntryCollaborators`, `ImportBatches`, `ImportRows`, `Judges`, `Invitations`, `JudgeImportBatches`, `JudgeImportRows` |
| `judging` | Judging module | `TastingTables`, `TableJudges`, `TableSamples`, `Evaluations`, `DiscrepancyAlerts` |
| `publication` | Publication module | `GeneratedScoreSheets`, `ResultsArchives` |
| `jobs` | `BuildingBlocks.Jobs` (set by the host) | `DispatchJobs` |
| `audit` | `BuildingBlocks.Audit` (set by the host) | `AuditLogs` |
| `migrations` | EF Core migration history | `__EFMigrationsHistory` |
| `public` | — | empty (no application table) |

## Rules

- Ownership follows the entity's module namespace (`BirraPoint.Api.Modules.<Module>.*`); building-block
  schemas are set by the host when it maps the block.
- Cross-schema foreign keys stay as they are today, for example `judging.TableSamples → organization.BeerEntries`,
  `organization.CompetitionCategoryStyles → catalog.BjcpStyles` and `jobs.DispatchJobs.CompetitionId →
  organization.Competitions` with cascade delete. Removing them is #107.
- Table names do not change (raw SQL and the job worker keep working once schema-qualified).

## Migration history

| State | History location | Startup behaviour |
|---|---|---|
| Empty database | none | Baseline applied (US15) |
| Built from the baseline | `migrations.__EFMigrationsHistory` with only `InitialCreate` (and later ids known to the assembly) | Normal migrate |
| Built from the old 14 migrations | `public.__EFMigrationsHistory` present | Startup stops and names the reset command (FR-070) |
