# Research: One data context per module, no cross-module foreign keys

**Feature**: [spec.md](./spec.md) | **Issue**: #107 | **Date**: 2026-11-27

## R-107-0 — Starting point (inventory)

- Code already crosses modules only through `Contracts/` (ADR-0033/0034). The only internal
  cross-module references are EF configurations under `Modules/<M>/Persistence/` that declare
  foreign keys (`HasOne<Competition>()`, `HasOne<BeerEntry>()`, …), exempted today by
  `ModuleBoundaryTests`.
- 12 cross-schema foreign keys in `InitialCreate`:

| From | Column | To | On delete |
|---|---|---|---|
| `jobs.DispatchJobs` | `CompetitionId` | `organization.Competitions` | Cascade |
| `publication.ResultsArchives` | `CompetitionId` | `organization.Competitions` | Cascade |
| `publication.GeneratedScoreSheets` | `BeerEntryId` | `organization.BeerEntries` | Cascade |
| `organization.CompetitionCategoryStyles` | `StyleCode` | `catalog.BjcpStyles` | Restrict |
| `organization.BeerEntries` | `StyleCode` | `catalog.BjcpStyles` | Restrict |
| `judging.TastingTables` | `CompetitionId` | `organization.Competitions` | Cascade |
| `judging.TableJudges` | `JudgeId` | `organization.Judges` | Cascade |
| `judging.TableSamples` | `BeerEntryId` | `organization.BeerEntries` | Cascade |
| `judging.Evaluations` | `BeerEntryId`, `JudgeId` | `organization.BeerEntries`, `organization.Judges` | Cascade |
| `judging.DiscrepancyAlerts` | `BeerEntryId` | `organization.BeerEntries` | Cascade |

- No flow deletes a competition, entry, judge or style (only import batches, categories in `Draft`
  and table rows inside Judging are deleted), so no cascade ever fires across modules: removing them
  changes no behaviour (spec Assumptions).
- Non-module users of `AppDbContext`: `CompetitionHub` (joins `TableJudges` with `Judges`, reads
  `Competitions`), `DatabaseHealthCheck`, startup migration, design-time factory, `Entity.cs` doc.
- Building blocks are generic over one `TContext`: `AddAudit<TContext,…>`, `AddJobs<TContext>`,
  `AddMessaging<TContext>` (one publisher, one dispatcher, one inbox runner over all outboxes/inboxes).
- Transactions that span blocks today: audit entries and staged jobs are added to the shared context
  and committed by the module's single `SaveChangesAsync`; the job worker runs the handler inside its
  own transaction (job state + handler effect commit together); the inbox runner commits inbox row +
  reaction together; `CloseTable`/`FixOrder`/`LiveJudgeRemoval` hold `FOR UPDATE` locks while reading
  Organization through contracts.
- 44 test files reference `AppDbContext` (fixtures, seeding, assertions).

## R-107-1 — One context class per store, shared base for stamping and history

**Decision**: abstract `BirraPointDbContext` in `Common/Persistence/` (timestamp stamping for
`ITimestamped` and `Job`, history table in the context's own schema, `HasDefaultSchema(schema)`).
Concrete contexts:

| Context | File | Schema | Maps |
|---|---|---|---|
| `CatalogDbContext` | `Modules/Catalog/Persistence/` | `catalog` | `BjcpStyles` |
| `OrganizationDbContext` | `Modules/Organization/Persistence/` | `organization` | its 13 tables + outbox + inbox |
| `JudgingDbContext` | `Modules/Judging/Persistence/` | `judging` | its 5 tables + outbox + inbox |
| `PublicationDbContext` | `Modules/Publication/Persistence/` | `publication` | its 2 tables + inbox |
| `JobsDbContext` | `Common/Persistence/Jobs/` | `jobs` | `DispatchJobs` |
| `AuditDbContext` | `Common/Persistence/Audit/` | `audit` | `AuditLogs` |

Each module context applies only the configurations of its own namespace
(`ApplyConfigurationsFromAssembly(assembly, t => t.Namespace starts with Modules.<M>.Persistence)`),
so a configuration can never pull another module's entity into the model. The
`MapModuleEntitiesToSchemas` namespace mapping becomes a model check (R-107-7).

The Jobs and Audit contexts live in the host (not in the blocks) because their table names, schema,
column rename (`GroupKey` → `CompetitionId`) and migrations are host decisions; the blocks stay
BirraPoint-free and keep their `ApplyJobs`/`ApplyAuditLog` model extensions.

**Alternatives**: contexts inside the building blocks — rejected (migrations would ship in reusable
blocks); one base context with conditional model — rejected (one model again).

## R-107-2 — Shared connection and transaction per scope (FR-096)

**Decision**: one scoped `NpgsqlConnection` per DI scope, created from the registered
`NpgsqlDataSource` (pooling unchanged), passed to every context with
`UseNpgsql(connection)` (context does not own it). A scoped `SharedTransaction` holder plus two
interceptors registered on every context:

- `SharedTransactionInterceptor : DbTransactionInterceptor` — when any context in the scope begins a
  transaction, it is recorded in the holder; on commit/rollback it is cleared.
- `UnitOfWorkSaveChangesInterceptor : SaveChangesInterceptor` —
  1. *SavingChanges* on any context: if the holder has a transaction and the context has none, call
     `Database.UseTransaction(holder.Transaction)`;
  2. if other contexts in the scope (the "participants": Audit, Jobs, or any module context with
     pending changes) have pending changes and no transaction exists, begin one on the saving context
     (marked "owned by the unit of work");
  3. *SavedChanges*: save every participant with pending changes (enlisted in the same transaction),
     then commit if the unit of work owns it; *SaveChangesFailed*: roll back if owned.

Reads through other modules' contracts during an explicit transaction need no enlistment: Npgsql runs
every command on the connection inside its active transaction. Enlistment is only needed for writes
(EF otherwise tries to begin a second transaction).

Effect: handler code keeps calling `dbContext.SaveChangesAsync()` once; staged audit entries, staged
jobs and outbox rows commit with it; the job worker and the inbox runner keep their atomicity because
their transaction is the scope's shared one.

**Alternatives**:
- Explicit `IUnitOfWork.SaveChangesAsync()` called by every handler — rejected: ~60 call sites, easy
  to forget the audit context.
- `TransactionScope` / ambient `System.Transactions` — rejected: Npgsql enlistment works but escalation
  rules and async flow are fragile; no distributed transaction wanted.
- Audit and jobs written through events — rejected in clarify (Q3).

## R-107-3 — Messaging block resolves a context per outbox/inbox

**Decision**: the registry maps each outbox/inbox name to a context type:
`MessagingBuilder.Outbox<TContext>(name)` and `.Inbox<TContext>(name)`; `AddMessaging(section,
configure)` loses its `TContext` parameter. The publisher resolves the outbox's context from the
current scope (the same instance the module saves, so the row commits with the change); the
dispatcher, the purger and the inbox runner resolve `(DbContext)scope.GetRequiredService(type)` per
name. Model-derived SQL (`OutboxSql`/`InboxSql`) already takes a context instance. Block tests gain a
two-context case. The block stays BirraPoint-free (FR-087) and transport-agnostic (FR-086).

**Alternatives**: one `AddMessaging<TContext>` per module — rejected: four dispatchers and a registry
per context break the single event registry and cross-context routing.

## R-107-4 — Migrations: one baseline per context, history in its own schema

**Decision**: delete `Common/Persistence/Migrations/`. Each context gets `InitialCreate` in its own
folder (`Modules/<M>/Persistence/Migrations/`, `Common/Persistence/Jobs/Migrations/`,
`Common/Persistence/Audit/Migrations/`), with `MigrationsHistoryTable("__EFMigrationsHistory",
schema)`. The BJCP seed (`InsertData` from `bjcp-2021.json`) moves to Catalog's baseline. The
`migrations` schema is no longer created. Commands:

```bash
dotnet ef migrations add <Name> --project src/BirraPoint.Api \
  --context OrganizationDbContext --output-dir Modules/Organization/Persistence/Migrations
```

Design time: one generic `DesignTimeDbContextFactory<TContext>` base with six thin subclasses.

**Alternatives**: keep one history table with a context column — not supported by EF.

## R-107-5 — Startup order, lock and legacy guard (FR-097, FR-098)

**Decision**: `StartupMigrations.ContextsInOrder = [Jobs, Audit, Catalog, Organization, Judging,
Publication]`; startup runs the guard once, then `MigrateAsync` per context in that order over the
migration connection string (dbDirect when set). EF Core 9+ takes a migration lock per history table
(Npgsql: lock on the history table inside the migration transaction), so concurrent replicas
serialise per context. A failure stops startup and logs the context name; earlier contexts stay
migrated.

The guard (FR-070/FR-091/FR-098) refuses when `public.__EFMigrationsHistory` **or**
`migrations.__EFMigrationsHistory` exists, and per context keeps the FR-091 rule (unknown applied ids
older than the earliest known id are refused, newer ones allowed).

**Alternatives**: drop the old history automatically — rejected (#105 decision: never delete data
without asking).

## R-107-6 — Validating ids written across modules (FR-093)

Checks already done today through contracts stay; missing ones are added where an FK was the only
guarantee:

| Write | Reference | Check |
|---|---|---|
| Organization category styles, entries (import consolidation, row edit) | `StyleCode` | `IStyleCatalog` (already validates, keeps `400`) |
| Judging create/update table | `CompetitionId`, `JudgeId`, `BeerEntryId` | `ICompetitionAccess.GetOwnedAsync`, `IJudgeDirectory.GetCompetitionJudgeIdsAsync`, `IEntryDirectory` (already scoped to the competition, `404`/`400` as today) |
| Judging evaluations, alerts | `BeerEntryId`, `JudgeId` | derived from the table's own `TableSamples`/`TableJudges` rows (already) |
| Publication sheets, archive | `BeerEntryId`, `CompetitionId` | derived from `IResultRecipients` / the job's group key (already) |
| Jobs | `CompetitionId` | set by the staging module from its own data |

The plan task verifies each path with a test that sends an unknown id and expects today's response.

## R-107-7 — Architecture and database tests (FR-099)

**Decision**:
- `ModuleBoundaryTests`: remove the `Modules/<M>/Persistence/` exemption and the `Common/Persistence/`
  composition exemption; a module may not reference another module's `Domain`, `Persistence`,
  `Features`, `Integration` namespaces or its `*DbContext` (contracts only).
- New `DbContextModelTests` (unit, no DB): for each context, every entity type's schema equals the
  context's schema, every CLR type belongs to the context's module namespace or its building block,
  and no foreign key targets an entity outside the model.
- New integration test: `information_schema.table_constraints`/`referential_constraints` query returns
  0 foreign keys whose referenced schema differs from the owning schema; every schema has its own
  `__EFMigrationsHistory`; `migrations` schema absent.

## R-107-8 — Non-module consumers

- `CompetitionHub.JoinCompetitionAsOrganizer` → `ICompetitionAccess.GetOwnedAsync`.
- `CompetitionHub.JoinTable` → new Judging contract `ITableMembership.IsActiveMemberAsync(tableId, sub,
  email)`, implemented in Judging, which resolves the caller's judge ids through a new
  `IJudgeDirectory.FindJudgeIdsAsync(sub, email)` (same `KeycloakUserId`/email fallback rule as today).
- `DatabaseHealthCheck` → the scoped `NpgsqlConnection` (`SELECT 1`), no context.
- Publication's dispatch status/retry read `DispatchJobs` through `JobsDbContext` (building block, not a
  module; allowed).

## R-107-9 — Tests and fixtures

The integration fixture resets every schema except `catalog` (same as today) and exposes a helper
`Db<TContext>()` scope; seeding helpers write through the owning context. Tests that asserted rows
across modules in one query split into per-context queries. No behaviour assertion changes (SC-026).

## R-107-10 — Performance (SC-028)

One connection per scope (unchanged count); six model builds at startup instead of one (cached after
first use, ~tens of ms each); six history checks at startup. Expected startup delta < 1 s. k6 budgets
re-run (`infra/perf/api-budgets.js`).
