# Implementation Plan: Database reset, single baseline migration, one schema per module

**Branch**: `feature/105-db-reset-module-schemas` | **Date**: 2026-11-08 | **Spec**: [spec.md](./spec.md) | **Issue**: #105

## Summary

Every application table moves into its owner's PostgreSQL schema: `catalog`, `organization`,
`judging` and `publication` for the modules, `jobs` and `audit` for the building blocks. The
migration history moves to `migrations`. The 14 migrations are replaced by one `InitialCreate` that
still seeds the BJCP catalog. Startup refuses a database built from the old migrations, and an
Aspire command resets only the BirraPoint database, so Keycloak's accounts survive. Behaviour,
contracts and existing tests do not change. Approach details are in [research.md](./research.md),
the schema map in [data-model.md](./data-model.md).

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: EF Core + Npgsql (existing), Aspire.Hosting.PostgreSQL (existing). No new packages.
**Storage**: PostgreSQL 16 (local Aspire container; Neon in the cloud, never created yet)
**Testing**: xUnit; integration with Testcontainers PostgreSQL (`SchemaTests`, `ApiFactory`, `StartupMigrationApiTests`); block tests for Jobs and Audit; Playwright E2E unchanged
**Target Platform**: ASP.NET Core API container; Aspire AppHost locally
**Project Type**: Web service (backend only; frontend untouched)
**Performance Goals**: unchanged (reads p95 < 200 ms, writes < 500 ms); schema-qualified names have no runtime cost
**Constraints**: no behaviour or contract change (FR-074); Keycloak database untouched (FR-071); building blocks stay BirraPoint-free (ADR-0035/0037)
**Scale/Scope**: 23 tables, 6 schemas + history; 5 raw-SQL lock queries; 14 migrations replaced by 1

## Constitution Check

*GATE: passes before Phase 0, re-checked after Phase 1. Constitution v1.6.0.*

| Principle | Check | Status |
|---|---|---|
| I. Spec-Driven | Issue #105 → approved spec → this plan → tasks; no requirement decided in code | ✅ |
| II. Modular monolith / slices | Schema per module makes ownership explicit; slices untouched except schema-qualified lock SQL | ✅ |
| III. Test-first | New tests first and failing: schema mapping, legacy-database guard, block schema overrides, history location | ✅ |
| IV–VI. Invariants (anonymity, idempotent sync, immutability, state machine, scoring) | No change; existing tests prove it (FR-074) | ✅ |
| VII. Security | No secrets, no new endpoint; reset command is a local Aspire command, never in the cloud | ✅ |
| VIII. Accessibility | No UI change | ✅ n/a |
| IX. Performance / offline | No runtime change | ✅ |
| X. Docs as part of done | ADR, baseline `data-model.md`, `quickstart.md`, living doc, `CLAUDE.md` (reset command) | ✅ |
| Stack | No new dependency | ✅ |

Post-design re-check: still all ✅. No complexity-tracking entries.

## Project Structure

### Documentation (this feature)

```text
specs/105-db-reset-module-schemas/
├── spec.md
├── plan.md            # this file
├── research.md        # R-105-1 … R-105-8
├── data-model.md      # schema map
├── quickstart.md      # validation scenarios
├── checklists/requirements.md
└── tasks.md           # /speckit-tasks
```

No `contracts/`: no API, SignalR or file contract changes.

### Source code touched

```text
src/BirraPoint.Api/
├── Common/Persistence/
│   ├── AppDbContext.cs              # schema convention, jobs/audit schemas, history table (OnConfiguring)
│   ├── DbSchemas.cs                 # NEW: schema name constants
│   ├── LegacyDatabaseGuard.cs       # NEW: FR-070 startup check
│   └── Migrations/                  # 14 migrations + snapshot deleted; NEW *_InitialCreate (+ BJCP seed) + snapshot
├── Program.cs                       # run LegacyDatabaseGuard before MigrateAsync
└── Modules/
    ├── Judging/Features/Evaluations/Features/CloseTable/CloseTableCommand.cs       # judging."TastingTables"
    ├── Judging/Features/TastingOrder/Features/FixOrder/FixOrderCommand.cs          # judging."TastingTables"
    ├── Judging/Features/Tables/Features/UpdateTable/LiveJudgeRemoval.cs            # judging."TastingTables"
    ├── Organization/Features/Competitions/Features/ChangeCompetitionState/ChangeCompetitionStateCommand.cs  # organization."Competitions"
    └── Organization/Features/Judges/Features/NotifyJudges/NotifyJudgesCommand.cs   # organization."Competitions"
src/Aspire/BirraPoint.AppHost/AppHost.cs   # "Reset database" command on the db resource
tests/
├── BirraPoint.Api.UnitTests/Persistence/LegacyDatabaseGuardTests.cs      # NEW (decision logic)
├── BirraPoint.Api.IntegrationTests/Persistence/SchemaTests.cs            # + every table in owner schema, history location, single migration
├── BirraPoint.Api.IntegrationTests/Persistence/LegacyDatabaseTests.cs    # NEW: old history → startup refuses
└── BuildingBlocks/{BuildingBlocks.Jobs.Tests,BuildingBlocks.Audit.Tests} # + host-set schema honoured (claim runs in a custom schema)
Docs/adrs/0041-schema-per-module.md        # NEW
specs/001-birrapoint-mvp/data-model.md     # schema per table
specs/001-birrapoint-mvp/quickstart.md, Docs/arquitectura_viva.md, CLAUDE.md   # reset command, schemas
```

**Structure Decision**: Backend only, existing layout (ADR-0033/0034). Migrations stay in
`Common/Persistence/Migrations` while there is one context (#107 moves them per module).

## Delivery order

1. Tests first (failing): schema mapping and history location (`SchemaTests`), legacy guard (unit and integration), block schema overrides.
2. Reference SQL script from the current migrations (R-105-5, step 1).
3. Schema convention + `DbSchemas` + history table + block schemas; qualify the 5 lock queries.
4. Delete old migrations, generate `InitialCreate`, port the BJCP seed, diff against the reference.
5. `LegacyDatabaseGuard` + Program.cs wiring.
6. AppHost **Reset database** command (fallback script if needed, R-105-7).
7. Full gates: build, format, unit, integration, block tests, E2E against the stack; `migration-safety` skill on the new migration.
8. Docs: ADR-0041, baseline data-model, quickstart, living doc, CLAUDE.md.

## Agents

| Part | Agent |
|---|---|
| Persistence, guard, lock SQL, migration, AppHost command, unit tests, block tests | backend-engineer |
| Integration tests (`SchemaTests`, legacy database), E2E run | qa-engineer |
| ADR, data-model, quickstart, living doc, CLAUDE.md | docs-keeper |

## Risks

- **Developers with existing local data** lose it on reset. This is accepted (spec), and startup tells them how to reset.
- **OnConfiguring merge** of the history option (R-105-4) is not honoured. Fallback: a shared `UseNpgsql` extension; the tests catch it.
- **Aspire command can't restart `api`.** Fallback: the reset script plus a manual restart (R-105-7).
- **Missed raw SQL.** The integration tests of the lock paths fail loudly; a grep check is in the tasks.
