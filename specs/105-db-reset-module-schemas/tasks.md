# Tasks: Database reset, single baseline migration, one schema per module

**Input**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [quickstart.md](./quickstart.md) — issue #105
**Tests**: mandatory and first (constitution Principle III); each test task is verified failing before its implementation task.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- Task ids stay in this file, commits and the PR — never in code, comments or test names (cite FR-/SC-/US or the reason).

## Phase 1: Setup

- [ ] T001 Generate the reference SQL of the current 14 migrations (`dotnet ef migrations script -p src/BirraPoint.Api -o <scratchpad>/before.sql`) and keep it outside the repo for the equivalence diff (R-105-5)
- [ ] T002 [P] List every raw SQL use against application tables (`git grep -nE 'FromSql|ExecuteSql|SqlQuery' -- src`) and confirm it matches the five lock queries in plan.md; add any extra hit to T011

## Phase 2: Foundational — schema mapping (delivers US16, blocks US15/US17)

**Goal**: every table mapped to its owner's schema, history outside `public`, without new migrations yet.
**Independent test**: `SchemaTests` lists tables per schema against the data-model.md map.

### Tests first

- [ ] T003 [P] [US16] Model-level unit test in `tests/BirraPoint.Api.UnitTests/Common/Persistence/SchemaMappingTests.cs`: every entity type in `AppDbContext.Model` has the schema from data-model.md (module namespace → schema, `Job` → `jobs`, `AuditLog` → `audit`), none has a null/`public` schema, and the migrations history table is configured in `migrations` (SC-015, FR-068, FR-073)
- [ ] T004 [P] [US16] Integration test in `tests/BirraPoint.Api.IntegrationTests/Persistence/SchemaTests.cs`: after migrating, `information_schema.tables` matches the data-model.md map exactly, `public` holds no table, and `migrations."__EFMigrationsHistory"` exists (FR-068, FR-073)
- [ ] T005 [P] [US16] Block test in `tests/BuildingBlocks/BuildingBlocks.Jobs.Tests/`: a host that maps `Job` into a custom schema through `ApplyJobs(configure)` can enqueue, claim (`FOR UPDATE SKIP LOCKED`) and complete a job there (FR-069)
- [ ] T006 [P] [US16] Block test in `tests/BuildingBlocks/BuildingBlocks.Audit.Tests/`: a host-set schema through `ApplyAuditLog(configure)` is honoured when writing an entry (FR-069)

### Implementation

- [ ] T007 [US16] Create `src/BirraPoint.Api/Common/Persistence/DbSchemas.cs` with the schema-name constants (`catalog`, `organization`, `judging`, `publication`, `jobs`, `audit`, `migrations`)
- [ ] T008 [US16] In `AppDbContext.OnModelCreating` add the namespace → schema convention after `ApplyConfigurationsFromAssembly` (R-105-1); throw at model build if a module entity falls outside the known modules
- [ ] T009 [US16] In `AppDbContext.OnModelCreating` set `ToTable("DispatchJobs", DbSchemas.Jobs)` in the `ApplyJobs` lambda and `ToTable("AuditLogs", DbSchemas.Audit)` in the `ApplyAuditLog` lambda (R-105-2)
- [ ] T010 [US16] In `AppDbContext.OnConfiguring` set `UseNpgsql(o => o.MigrationsHistoryTable("__EFMigrationsHistory", DbSchemas.Migrations))`; verify it merges with every construction site (Program.cs, design-time factory, test fixtures) or fall back to a shared extension (R-105-4)
- [ ] T011 [US16] Schema-qualify the five row-lock queries: `judging."TastingTables"` in `CloseTableCommand.cs`, `FixOrderCommand.cs`, `LiveJudgeRemoval.cs`; `organization."Competitions"` in `ChangeCompetitionStateCommand.cs`, `NotifyJudgesCommand.cs` (R-105-3)

**Checkpoint**: T003, T005, T006 green. T004 needs the baseline (T013).

## Phase 3: User Story 15 — fresh database from a single baseline (P1) 🎯

**Goal**: one `InitialCreate` builds the full model + BJCP seed; equivalent to the old 14.
**Independent test**: quickstart §3.

### Tests first

- [ ] T012 [US15] Extend `SchemaTests`: applied migrations are exactly one id ending in `_InitialCreate`; `catalog."BjcpStyles"` holds the full bjcp-2021.json catalog count (FR-066, SC-018)

### Implementation

- [ ] T013 [US15] Delete `src/BirraPoint.Api/Common/Persistence/Migrations/*` (14 migrations + designer files + snapshot) and run `dotnet ef migrations add InitialCreate -p src/BirraPoint.Api -o Common/Persistence/Migrations`
- [ ] T014 [US15] Port the BJCP seed (embedded `bjcp-2021.json` reader + `InsertData`) from the deleted `AddBjcpStyleCatalogDetails` migration into `InitialCreate.Up`, with `schema: DbSchemas.Catalog` (FR-049, ADR-0005)
- [ ] T015 [US15] Generate `after.sql` and diff it against T001's `before.sql` with schema prefixes stripped; only the differences allowed by R-105-5 may remain. Record the result for the PR (FR-067)
- [ ] T016 [US15] Run the `migration-safety` skill on the new migration and fix its findings
- [ ] T017 [US15] Full backend gates: `dotnet build BirraPoint.sln`, `dotnet format --verify-no-changes`, unit, integration and block test suites green with no existing assertion changed (FR-074, SC-016)

**Checkpoint**: T004 and T012 green; US15 and US16 deliverable.

## Phase 4: User Story 17 — reset only the application data (P3)

**Goal**: old databases are refused with guidance; one command resets `birrapoint` and keeps Keycloak.
**Independent test**: quickstart §1 and §2.

### Tests first

- [ ] T018 [P] [US17] Unit tests in `tests/BirraPoint.Api.UnitTests/Common/Persistence/LegacyDatabaseGuardTests.cs` for the decision: old public history → refuse; unknown applied id → refuse; empty or baseline-only → proceed; the message names the reset command (FR-070)
- [ ] T019 [P] [US17] Integration test `tests/BirraPoint.Api.IntegrationTests/Persistence/LegacyDatabaseTests.cs`: a Testcontainers database with a `public."__EFMigrationsHistory"` row makes API startup fail with that message and creates no table in the module schemas (FR-070)

### Implementation

- [ ] T020 [US17] Implement `src/BirraPoint.Api/Common/Persistence/LegacyDatabaseGuard.cs` (R-105-6) and call it in `Program.cs` before `MigrateAsync`, on the same migration connection (`StartupMigrations.ResolveMigrationConnectionString`)
- [ ] T021 [US17] Add the **Reset database** command to the `db` resource in `src/Aspire/BirraPoint.AppHost/AppHost.cs`: `DROP DATABASE "birrapoint" WITH (FORCE)` + `CREATE DATABASE` on the maintenance database, then restart `api`; never touches `keycloak` (R-105-7). If not feasible, `scripts/reset-db.ps1` fallback documented instead
- [ ] T022 [US17] Manual validation with the running stack: quickstart §1 (old volume refused) and §2 (reset; pre-existing Keycloak account still signs in) (SC-017)

## Phase 5: Polish & docs

- [ ] T023 [P] E2E suite against the reset stack: `cd src/Birrapoint.Web && npm run e2e` green (SC-016)
- [ ] T024 [P] docs-keeper: ADR `Docs/adrs/0041-schema-per-module.md`; schema per table in `specs/001-birrapoint-mvp/data-model.md`; reset command in `specs/001-birrapoint-mvp/quickstart.md` and `CLAUDE.md` Commands; living doc (persistence section, schemas) (FR-075)
- [ ] T025 Run quickstart.md §1–§5 end to end and mark this file's tasks done

## Dependencies

- T001 before T013 (reference script needs the old migrations). T002 feeds T011.
- Phase 2 (T007–T011) before T013: the baseline is generated from the schema-mapped model.
- T013 → T014 → T015 → T016 → T017.
- US17 (T018–T022) needs T013 (the guard distinguishes old vs. baseline history). T021 is independent of T020.
- T023–T025 last.

## Parallel examples

- Tests first: T003, T004, T005, T006 together; later T018 and T019 together.
- backend-engineer on T007–T011 while qa-engineer writes T004/T012/T019.
- T024 (docs) in parallel with T022/T023.

## Implementation strategy

MVP is Phase 2 + Phase 3 (US16 + US15): schemas and the baseline. Phase 4 adds the safety net
(guard + reset) before merging, because without it every developer's existing local volume would
migrate into a silent mix of old and new tables.
