# 0043 - One data context per module, shared connection and transaction

**Status:** Accepted
**Date:** 2026-11-27

## Context
ADR-0041 gave each module a schema, but one `AppDbContext` still mapped every module. Modules shared
one model, one migration history and cross-schema foreign keys, so they could not be extracted as
services. `CompetitionHub` also read `DbSet`s of several modules. A handler's single `SaveChangesAsync`
must still commit the module row, its audit entry, a staged job and an outbox row atomically
(ADR-0024, ADR-0035, ADR-0042). Issue #107.

## Decision
- **Six contexts**, one schema each, built on an abstract `BirraPointDbContext` (timestamp stamping,
  history table in the context's own schema): `CatalogDbContext`, `OrganizationDbContext`,
  `JudgingDbContext`, `PublicationDbContext` in `Modules/<M>/Persistence/`; `JobsDbContext` and
  `AuditDbContext` in `Common/Persistence/{Jobs,Audit}/` (host-owned, so the building blocks ship no
  migrations). Each applies only its own namespace's configurations.
- **Own migrations**: each context has its own `InitialCreate`, snapshot and
  `<schema>.__EFMigrationsHistory`. Startup migrates Jobs, Audit, Catalog, Organization, Judging,
  Publication in that order. `LegacyDatabaseGuard` refuses a database with the old shared history
  (`public` or `migrations`); recovery is the Reset database command (ADR-0041).
- **Ids only across modules**: the 12 cross-schema FKs are dropped. References are validated on write
  through the owner's `Contracts/`. Explicit indexes replace the ones the FKs created.
- **One unit of work**: one scoped `NpgsqlConnection` per DI scope is shared by all contexts.
  `SharedTransactionInterceptor` records the active transaction and `UnitOfWorkSaveChangesInterceptor`
  enlists the other contexts with pending changes, so one `SaveChangesAsync` commits all of them.
  A failed participant save rolls the transaction back once and surfaces the original
  `DbUpdateException`. EF does not call `SaveChangesFailed` after a `DbUpdateConcurrencyException`
  (and cannot roll back there, the reader is still open), so a transaction the unit of work still owns
  is rolled back at the start of the next save on any context of the scope, or when the scope is
  disposed.
- **Registration**: `AddBirraPointDbContext<T>` is idempotent.
- **Messaging**: `Outbox<TContext>(name)` / `Inbox<TContext>(name)` bind each outbox/inbox to its
  context; `AddMessaging(section, configure)` has no context parameter.
  `OutboxWakeUp` tracks pending outbox rows per `DbConnection`, so a commit on any context sharing the
  connection wakes the dispatcher.
- **Hub**: `CompetitionHub` uses `ICompetitionAccess` and the new Judging contract `ITableMembership`
  instead of touching `DbSet`s.

## Consequences
- Easier: module boundary enforced by the model (`DbContextModelTests`), per-module migrations, a
  later split into services needs no schema surgery.
- Harder: no database-enforced referential integrity across modules; orphan ids are possible if a
  deletion path skips the owner's contract. Joins across modules become two reads.
- Existing cloud databases need a one-off manual reset after this baseline change: see "After a
  migration-baseline change" in `infra/azure/deployment-runbook.md` and `infra/aws/deployment-runbook.md`.
- Six model builds and six history checks at startup (expected < 1 s).
- Rejected: contexts inside the building blocks (migrations in reusable code); one `AddMessaging` per
  module (four dispatchers, no single event registry); an explicit `IUnitOfWork` called by every
  handler (about 60 call sites, easy to forget the audit write); `TransactionScope` (fragile
  escalation and async flow); writing audit and jobs through events (not atomic).
- Open: no distributed transaction, so the model works only while modules share one database and
  connection; a real split needs the outbox for every cross-module write.
