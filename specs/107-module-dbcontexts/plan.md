# Implementation Plan: One data context per module, no cross-module foreign keys

**Branch**: `feature/107-module-dbcontexts` | **Date**: 2026-11-27 | **Spec**: [spec.md](./spec.md) | **Issue**: #107

## Summary

The shared `AppDbContext` is split into six contexts: one per module (Catalog, Organization,
Judging, Publication) and one each for the Jobs and Audit building blocks. Each context maps only its
own schema and has its own baseline migration and `__EFMigrationsHistory` in that schema. The 12
cross-schema foreign keys are dropped; references become plain ids, validated on write through the
owner's contracts (they already are on every path; tests prove it).

Within one DI scope all contexts share one `NpgsqlConnection`. Two interceptors make any context's
transaction the scope's transaction and save the other contexts' pending changes (audit entries,
staged jobs) inside it, so handler code keeps one `SaveChangesAsync` and FR-096 atomicity holds. The
Messaging block maps each outbox/inbox to its context type. `CompetitionHub` and the health check stop
using a context directly. Existing databases are refused at startup and reset (clarify Q2).
Decisions: [research.md](./research.md).

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: EF Core + Npgsql, MediatR 12.5, Aspire. No new packages.
**Storage**: PostgreSQL 16, same tables and columns; six baseline migrations replace the shared two; cross-schema FKs removed; `migrations` schema gone
**Testing**: xUnit unit (model/architecture tests, interceptor unit-of-work behaviour, legacy guard), Testcontainers integration (cross-schema FK query, per-schema history, atomicity across contexts, unknown-id writes, every existing suite), Messaging block tests (two-context case); Playwright E2E unchanged and re-run
**Target Platform**: ASP.NET Core API container; Aspire locally
**Project Type**: Web service (no frontend change)
**Performance Goals**: REST budgets unchanged; startup +≤ 2 s (SC-028)
**Constraints**: building blocks stay BirraPoint-free (ADR-0035/0037); one connection per scope (FR-096); still one API and one database; no Judging local copy (clarify Q1)
**Scale/Scope**: 6 contexts, ~45 handler/integration files retargeted (mechanical), 3 block registration APIs, ~44 test files

## Constitution Check

*GATE: passes before Phase 0, re-checked after Phase 1. Constitution v1.6.0.*

| Principle | Check | Status |
|---|---|---|
| I. Spec-Driven | Issue #107 → approved spec (clarified 2026-11-27) → this plan; living `data-model.md` amended before code | ✅ |
| II. Modular monolith / slices | Each module owns one context; boundary tests lose the Persistence exemption; slices unchanged in shape | ✅ |
| III. Test-first | Architecture/model tests, FK query, atomicity and unknown-id tests written first and failing | ✅ |
| IV. Blind anonymity | No DTO or payload change; Judging still reads only anonymous contract fields | ✅ |
| V. Idempotency / immutability / state machine | Unique `(JudgeId, BeerEntryId)` stays (same schema); row locks unchanged; audit stays in the same transaction | ✅ |
| VI. Contract-first | No REST/SignalR change; `data-model.md` (FKs, schemas, histories) amended first | ✅ |
| VII. Security | Hub join authorization keeps the same rule through contracts; deny-by-default unchanged | ✅ |
| VIII. Accessibility | No UI change | ✅ |
| IX. Performance / offline | Same connection count; budgets re-run | ✅ |
| X. Docs as part of done | ADR-0043, living doc, CLAUDE.md commands/layout, `migration-safety` skill paths | ✅ |
| Stack | No new dependency | ✅ |

Post-design re-check: all ✅. No complexity-tracking entries (the split is the issue's goal).

## Project Structure

### Documentation (this feature)

```text
specs/107-module-dbcontexts/
├── spec.md
├── plan.md            # this file
├── research.md        # R-107-0 … R-107-10
├── data-model.md      # pointer to the living data model + context map
├── quickstart.md      # validation scenarios
├── checklists/requirements.md
└── tasks.md
```

Living contract amended in place: `specs/001-birrapoint-mvp/data-model.md` (schemas, histories,
cross-module references as ids). No REST, SignalR, import or integration-event contract change.

### Source code touched

```text
src/BuildingBlocks/BuildingBlocks.Messaging/
├── MessagingBuilder.cs / MessagingRegistry.cs              # Outbox<TContext>(name), Inbox<TContext>(name)
├── MessagingServiceCollectionExtensions.cs                 # AddMessaging(section, configure) — no TContext
├── IntegrationEventPublisher.cs / InboxRunner.cs / OutboxDispatcher.cs / RetentionPurger.cs / LocalTransport.cs
│                                                            # resolve the context per outbox/inbox name
src/BirraPoint.Api/
├── Program.cs                                              # data source, scoped connection, six AddDbContext, blocks
├── Common/Persistence/
│   ├── AppDbContext.cs, DesignTimeDbContextFactory.cs, Migrations/   # DELETED
│   ├── BirraPointDbContext.cs                              # NEW abstract base: stamping, schema, history table
│   ├── SharedConnection.cs / SharedTransaction.cs          # NEW scoped connection + transaction holder
│   ├── SharedTransactionInterceptor.cs / UnitOfWorkSaveChangesInterceptor.cs   # NEW (R-107-2)
│   ├── DbContextRegistration.cs                            # NEW AddBirraPointDbContext<T>() helper
│   ├── DesignTimeDbContextFactory.cs                       # NEW generic base + per-context subclasses
│   ├── StartupMigrations.cs                                # ContextsInOrder + MigrateAllAsync
│   ├── LegacyDatabaseGuard.cs                              # FR-098: refuse migrations.__EFMigrationsHistory
│   ├── DatabaseHealthCheck.cs                              # connection, no context
│   ├── DbSchemas.cs                                        # drop Migrations
│   ├── Jobs/JobsDbContext.cs + Migrations/                 # NEW
│   └── Audit/AuditDbContext.cs + Migrations/               # NEW
├── Domain/Entity.cs                                        # doc comment
├── Realtime/CompetitionHub.cs                              # contracts instead of a context
├── Modules/Catalog/Persistence/CatalogDbContext.cs + Migrations/ (BJCP seed)       # NEW
├── Modules/Organization/Persistence/OrganizationDbContext.cs + Migrations/        # NEW
│   Persistence/BeerEntryConfiguration.cs, CompetitionCategoryStyleConfiguration.cs # drop FK to BjcpStyle
│   Contracts/IJudgeDirectory.cs + Integration/JudgeDirectory                       # + FindJudgeIdsAsync(sub, email)
├── Modules/Judging/Persistence/JudgingDbContext.cs + Migrations/                  # NEW
│   Persistence/*Configuration.cs                                                  # drop FKs to Organization
│   Contracts/ITableMembership.cs + Integration/TableMembership.cs                 # NEW (hub join)
├── Modules/Publication/Persistence/PublicationDbContext.cs + Migrations/          # NEW
│   Persistence/*Configuration.cs                                                  # drop FKs to Organization
├── Modules/<M>/<M>Module.cs                                # register context, messaging Outbox/Inbox
└── Modules/**/{Features,Integration}/**/*.cs               # AppDbContext → <M>DbContext (mechanical)
tests/
├── BirraPoint.Api.UnitTests/Architecture/ModuleBoundaryTests.cs, ModuleBoundaryScanner.cs  # no Persistence exemption
├── BirraPoint.Api.UnitTests/Architecture/DbContextModelTests.cs                            # NEW
├── BirraPoint.Api.UnitTests/Common/Persistence/{LegacyDatabaseGuardTests,StartupMigrationsTests}.cs
├── BirraPoint.Api.IntegrationTests/Persistence/{SchemaIsolationTests,SharedTransactionTests,CrossModuleReferenceTests}.cs  # NEW
├── BirraPoint.Api.IntegrationTests/TestHost/*                                              # fixture per-context helpers
├── BirraPoint.Api.IntegrationTests/**                                                       # AppDbContext → owning context
└── BuildingBlocks/BuildingBlocks.Messaging.Tests                                            # two-context routing tests
```

The unit-of-work interceptor is tested against a real Postgres (Testcontainers) in the integration
project, since the constitution bans EF InMemory and atomicity needs a real transaction.

## Phases

- **Phase 0 (research)**: [research.md](./research.md) — all decisions made, no open items.
- **Phase 1 (design)**: [data-model.md](./data-model.md) (context map; living model amended),
  [quickstart.md](./quickstart.md). No new contracts.
- **Phase 2**: [tasks.md](./tasks.md).

## Ownership

| Part | Agent |
|---|---|
| Messaging block refactor + block tests | backend-engineer |
| Contexts, interceptors, migrations, startup, guard, module retargeting, hub, unit/architecture tests | backend-engineer |
| Integration tests (schema isolation, shared transaction, unknown ids), fixture retarget, E2E + k6 re-run | qa-engineer |
| ADR-0043, living doc, CLAUDE.md, `migration-safety` skill paths | docs-keeper |

No frontend or infra change: Terraform keeps `Database:MigrateOnStartup`; the connection strings are
unchanged.

## Risks

- **Interceptor misses a participant** (audit row lost while the module row commits): covered by the
  shared-transaction integration tests (rollback of the module save rolls back audit + job + outbox;
  commit commits all) for plain `SaveChanges`, explicit transactions, job worker and inbox runner.
- **Large mechanical diff**: retargeting is done with `AppDbContext` → owning context per folder; the
  PR's "How to review" says so.
- **EF model pulling foreign entities**: model tests fail if any context contains another module's
  entity.
