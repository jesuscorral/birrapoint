# Tasks: One data context per module, no cross-module foreign keys

**Input**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [quickstart.md](./quickstart.md); living [data-model.md](../001-birrapoint-mvp/data-model.md). Issue #107.
**Tests**: mandatory and first (constitution Principle III). Each test task is verified failing before its implementation task.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- Task ids stay in this file, commits and the PR. They never go in code, comments or test names; cite FR-/SC-/US/ADR or the reason instead.
- Paths: block `src/BuildingBlocks/BuildingBlocks.Messaging/` (= `BB.M/`), block tests `tests/BuildingBlocks/BuildingBlocks.Messaging.Tests/` (= `BB.M.Tests/`), API `src/BirraPoint.Api/` (= `Api/`), integration tests `tests/BirraPoint.Api.IntegrationTests/` (= `IT/`), unit tests `tests/BirraPoint.Api.UnitTests/` (= `UT/`).

## Phase 1: Foundational — Messaging block routes per context (blocks every story)

**Goal**: each outbox/inbox name maps to its own context type; the block no longer needs one shared context (R-107-3, FR-086/FR-087).
**Independent test**: `dotnet test tests/BuildingBlocks/BuildingBlocks.Messaging.Tests` green.

- [X] T001 [P] Update `BB.M.Tests/TestHost` to two test contexts (`ATestDbContext` schema `a` with outbox `A` + inbox `A`; `BTestDbContext` schema `b` with inbox `B`) sharing nothing but the database; add `BB.M.Tests/MultiContextRoutingTests.cs` (Testcontainers): an event published on A's context is stored in `a."OutboxMessages"` and commits with A's `SaveChangesAsync`; the dispatcher delivers it to a handler whose inbox lives in B, the inbox row lands in `b."InboxMessages"` with the handler's effect on B; the purger cleans both schemas; a `Outbox<T>`/`Inbox<T>` name used twice with different context types is rejected at registration
- [X] T002 Adapt the existing block tests (publisher, dispatcher, inbox runner, dead letter, wake-up, retention, transport) to the new registration API; verify T001 fails
- [X] T003 `BB.M/MessagingBuilder.cs` + `MessagingRegistry.cs`: `Outbox<TContext>(name)`, `Inbox<TContext>(name)` recording the context type per name; `Publishes<TEvent>(outbox)`/`Subscribe<TEvent,THandler>(inbox)` require a declared name
- [X] T004 `BB.M/MessagingServiceCollectionExtensions.cs`: `AddMessaging(section, configure)` without `TContext`; `IntegrationEventPublisher`, `InboxRunner`, `OutboxDispatcher`, `RetentionPurger`, `LocalTransport` resolve the context per outbox/inbox name from the scope (publisher uses the scoped instance so the row commits with the caller); doc comments updated; T001/T002 green

## Phase 2: Foundational — base context, shared connection, unit of work, building-block contexts

**Goal**: any number of contexts in one scope share one connection and one transaction, and one `SaveChangesAsync` commits every context's pending changes (R-107-1, R-107-2, FR-096, FR-100).

### Tests first

- [X] T005 [P] `IT/Persistence/SharedTransactionTests.cs` (Testcontainers, test-only contexts or the real Jobs/Audit + one module context once T011 lands — write against the real ones, compile-failing until then): (a) module save with a staged audit entry, staged job and outbox event commits all four; (b) a module save that fails (unique violation) leaves none of the four; (c) inside an explicit transaction begun on the module context, rollback leaves none, commit leaves all; (d) a transaction begun on `JobsDbContext` (job worker shape) commits the module context's handler writes with the job state; (e) one `NpgsqlConnection` instance is used by every context in the scope; (f) `CreatedAt`/`UpdatedAt` stamped on every context (FR-100) (written; enabled with the contexts)
- [X] T006 [P] `UT/Architecture/DbContextModelTests.cs`: for each of the six contexts (built with `UseNpgsql` and no connection), every entity type's schema equals the context's schema, every CLR type is in the context's module namespace (`Modules.<M>.`) or its building block (`BuildingBlocks.Jobs`/`Audit`/`Messaging`), no foreign key targets an entity outside the model, and the history table is `<schema>.__EFMigrationsHistory` (FR-092, FR-093, FR-097, FR-099)

### Implementation

- [X] T007 `Api/Common/Persistence/BirraPointDbContext.cs` (abstract): constructor takes the schema; `HasDefaultSchema`, history table in the schema, timestamp stamping moved from `AppDbContext` (`ITimestamped` + `Job`); `DbSchemas.cs` drops `Migrations`
- [X] T008 `Api/Common/Persistence/SharedConnection.cs`, `SharedTransaction.cs`, `SharedTransactionInterceptor.cs`, `UnitOfWorkSaveChangesInterceptor.cs` per R-107-2; `DbContextRegistration.cs` with `AddBirraPointDbContext<TContext>()` (scoped connection from `NpgsqlDataSource`, both interceptors, messaging interceptors)
- [X] T009 [P] `Api/Common/Persistence/Jobs/JobsDbContext.cs` (`ApplyJobs` with the table/column mapping from `AppDbContext`, no FK to `Competition`) and `Api/Common/Persistence/Audit/AuditDbContext.cs` (`ApplyAuditLog`, jsonb)
- [X] T010 [P] `Api/Common/Persistence/DesignTimeDbContextFactory.cs`: generic base `DesignTimeDbContextFactory<TContext>` + one subclass per context

## Phase 3: User Story 22 — Each module owns its data alone (P1) 🎯 MVP

**Goal**: four module contexts, no cross-module FK or navigation, every consumer on its own module's context, hub through contracts.
**Independent test**: architecture tests + `IT/Persistence/SchemaIsolationTests` + whole suites green.

### Tests first

- [X] T011 [P] [US22] `UT/Architecture/ModuleBoundaryTests.cs` + `ModuleBoundaryScanner.cs`: drop the `Modules/<M>/Persistence/` and `Common/Persistence/` exemptions; a module referencing another module's `Domain`, `Persistence`, `Features`, `Integration` namespace or any `*DbContext` other than its own fails, naming the type; `Realtime/` and `Common/` may use only `Contracts/`, building-block contexts and none of the module contexts; scanner self-tests cover the new rules (FR-094, FR-099)
- [X] T012 [P] [US22] `IT/Persistence/SchemaIsolationTests.cs`: the quickstart §1 query returns 0 rows; each module schema's tables are exactly the living data-model's list (SC-025)
- [X] T013 [P] [US22] `IT/Persistence/CrossModuleReferenceTests.cs`: with the FKs gone, unknown ids still get today's responses: consolidating a beer import row with an unknown style code (`409 urn:birrapoint:unresolved-import-rows`, no entry created), creating/updating a table with a judge id or entry id of another competition or unknown (`400` from the validators, as today), joining an unknown table on the hub (rejected) (FR-093, R-107-6)
- [X] T014 [P] [US22] `IT/Realtime/CompetitionHubJoinTests.cs`: organizer joins own competition, is rejected on a foreign one; judge joins a table where active (by Keycloak id and by email fallback), is rejected when removed or not seated (behaviour unchanged after moving to contracts; closes the hub-auth coverage gap in the living doc)

### Implementation

- [X] T015 [US22] `Api/Modules/Catalog/Persistence/CatalogDbContext.cs`; `Modules/Organization/Persistence/OrganizationDbContext.cs` (outbox + inbox `Organization`); `Modules/Judging/Persistence/JudgingDbContext.cs` (outbox + inbox `Judging`); `Modules/Publication/Persistence/PublicationDbContext.cs` (inbox `Publication`); each applies only its own namespace's configurations; delete `Common/Persistence/AppDbContext.cs`
- [X] T016 [US22] Drop every cross-module `HasOne<…>()` in `Modules/Organization/Persistence/{BeerEntry,CompetitionCategoryStyle}Configuration.cs`, `Modules/Judging/Persistence/*Configuration.cs`, `Modules/Publication/Persistence/*Configuration.cs` (indexes and unique constraints kept, R-107-0 table)
- [X] T017 [US22] Retarget every `AppDbContext` consumer under `Modules/<M>/` to `<M>DbContext` (Features, Integration, Identity, CommonComponents); Publication's dispatch status/retry and `DispatchJobQueries` use `JobsDbContext`; raw SQL keeps its schema-qualified names
- [X] T018 [US22] `Modules/<M>/<M>Module.cs`: register the module context with `AddBirraPointDbContext<T>()` and its messaging `Outbox<T>`/`Inbox<T>`; `Program.cs`: `NpgsqlDataSource`, Jobs/Audit contexts, `AddAudit<AuditDbContext,…>`, `AddJobs<JobsDbContext>`, `AddMessaging(section, …)`
- [X] T019 [US22] Hub through contracts (R-107-8): `Modules/Organization/Contracts/IJudgeDirectory.cs` + implementation gain `FindJudgeIdsAsync(sub, email)` (same `KeycloakUserId`/email fallback rule as the hub today); new `Modules/Judging/Contracts/ITableMembership.cs` + `Integration/TableMembership.cs`; `Realtime/CompetitionHub.cs` uses `ICompetitionAccess.GetOwnedAsync` and `ITableMembership`; `DatabaseHealthCheck.cs` uses the scoped connection; `Domain/Entity.cs` doc comment
- [X] T020 [US22] Generate the six baselines (`dotnet ef migrations add InitialCreate --context <C> --output-dir <folder>`): `Common/Persistence/Jobs/Migrations/`, `Common/Persistence/Audit/Migrations/`, `Modules/<M>/Persistence/Migrations/`; move the BJCP seed (`InsertData` from `bjcp-2021.json`) into Catalog's baseline; delete `Common/Persistence/Migrations/`; run the `migration-safety` skill on the result
- [X] T021 [US22] Unit tests touching `AppDbContext` (`UT/Common/**`, `UT/Modules/**`) retargeted to the owning context; T006 and T011 green
- [X] T022 [US22] Integration fixture `IT/TestHost/*`: migrate all contexts in order, reset every schema except `catalog`, per-context seeding/assertion helpers; retarget the ~40 integration test files to the owning context (no behaviour assertion changes, SC-026); T005, T012–T014 green

T013 and T014 are regression guards for behaviour that must not change: they pass on `main` and must
keep passing once the FKs and the hub's direct queries are gone; "verified failing" does not apply to them.

## Phase 4: User Story 23 — Each store migrates on its own (P2)

**Goal**: ordered startup migration per context, old shared history refused.
**Independent test**: quickstart §2/§3.

### Tests first

- [X] T023 [P] [US23] `UT/Common/Persistence/LegacyDatabaseGuardTests.cs`: `migrations.__EFMigrationsHistory` present ⇒ refused naming **Reset database** (FR-098); `public` history ⇒ refused (FR-070); per-context FR-091 rule (newer unknown id allowed, older refused) unchanged
- [X] T024 [P] [US23] `UT/Common/Persistence/StartupMigrationsTests.cs`: `ContextsInOrder` is Jobs, Audit, Catalog, Organization, Judging, Publication; `ShouldMigrate`/connection resolution unchanged
- [X] T025 [P] [US23] `IT/Persistence/StartupMigrationTests.cs`: on an empty database every schema has its own history with one `InitialCreate` row, `migrations` schema absent, BJCP seeded; a second start applies nothing; a database with a `migrations.__EFMigrationsHistory` table makes startup throw the reset message (SC-027) (written; enabled with the contexts)

### Implementation

- [X] T026 [US23] `Api/Common/Persistence/LegacyDatabaseGuard.cs`: FR-098 check; evaluate per context against its own history table
- [X] T027 [US23] `Api/Common/Persistence/StartupMigrations.cs`: `ContextsInOrder` + `MigrateAllAsync(IServiceProvider, IConfiguration)` (guard once, then each context over the migration connection string, logging the context name on failure); `Program.cs` calls it

## Phase 5: Polish & cross-cutting

- [ ] T028 Gates: `dotnet build BirraPoint.sln`, `dotnet format BirraPoint.sln --verify-no-changes`, unit + integration + block suites, `npm run e2e`, `.github/scripts/check-no-task-ids.sh`
- [ ] T029 [P] Quickstart §6 on the local stack (reset first) and §7 budgets (startup delta, k6); record results in the PR
- [X] T030 [P] Docs (`docs-keeper`): ADR-0043 (context per module, shared connection + unit-of-work interceptor, id-only references, per-context migrations and order, no Judging local copy); `Docs/arquitectura_viva.md` (persistence section, hub-auth gap closed, new debt if any); CLAUDE.md (§Layout persistence, §Commands `dotnet ef ... --context`); `.claude/skills/migration-safety` paths

## Dependencies

- Phase 1 and Phase 2 tests can start together; T003–T004 before T018.
- T007–T010 before T015; T015–T016 before T017–T020; T020 before any integration test can go green.
- US23 (Phase 4) needs T020; T026–T027 can land with T022.
- Phase 5 last.

## Parallel examples

- T001, T005, T006, T011–T014, T023–T025 (tests, different files) together.
- T009 and T010 together; T021 and T022 split across backend-engineer and qa-engineer.
