# Implementation Plan: Integration events between modules with a transactional outbox

**Branch**: `feature/106-integration-events-outbox` | **Date**: 2026-11-09 | **Spec**: [spec.md](./spec.md) | **Issue**: #106

## Summary

Also in scope: the purge of outbox/inbox rows after a retention period (FR-090), and the open legacy-guard finding from #105 (FR-091).

Modules stop writing each other's data in one transaction. A new building block,
`BuildingBlocks.Messaging`, gives each module:
- a publisher that stores integration events in the module's own `OutboxMessages` table, in the
  caller's transaction;
- a hosted dispatcher that delivers them at least once, using the Jobs block's
  claim/lease/backoff pattern, through a transport abstraction (in-process now, Dapr in #108);
- an inbox runner that records processed event ids in the subscriber's `InboxMessages` table,
  together with the reaction.

The two existing cross-module MediatR notifications become integration events
(`competition-finalized`, `table-judges-changed`), with order-insensitive handlers. The two debt
items are closed:
- `judge-email-changed`: BOS flags are recomputed in Organization's own transaction, and Judging
  alerts organizers through the new `JudgeConflictDetected` realtime event.
- `evaluation-corrected`: Publication's new `RefreshResults` job regenerates the PDF and the ZIP,
  with no emails.

The wake-up after commit keeps reactions ≤ 1 s. Decisions are in [research.md](./research.md);
contracts are in the living files listed in [data-model.md](./data-model.md).

## Technical Context

**Language/Version**: C# 14 / .NET 10; Angular 20 (one banner)
**Primary Dependencies**: EF Core + Npgsql, MediatR 12.5 (requests only from now on), SignalR. No new packages.
**Storage**: PostgreSQL 16. Five new tables (`organization`/`judging` outbox + inbox, `publication` inbox) in one migration.
**Testing**: xUnit + Testcontainers (new `BuildingBlocks.Messaging.Tests`, host integration tests per flow, latency test); unit tests for the retry policy, envelope serialization and the legacy guard fix; Jest for the banner; contract test for event payload anonymity
**Target Platform**: ASP.NET Core API container (single replica; multi-dispatcher safe); Aspire locally
**Project Type**: Web service + PWA
**Performance Goals**: commit-to-reaction p95 ≤ 1 s (FR-089/SC-020); REST budgets unchanged (writes now do less work in-transaction)
**Constraints**: building blocks stay BirraPoint-free (ADR-0035/0037); events carry no personal or entrant data (FR-085); emits after commit (FR-084); no broker (#108); shared `AppDbContext` kept (#107)
**Scale/Scope**: 4 event types, 5 subscribers' tables, ~10 slices touched, 1 new job type, 1 realtime event

## Constitution Check

*GATE: passes before Phase 0, re-checked after Phase 1. Constitution v1.6.0.*

| Principle | Check | Status |
|---|---|---|
| I. Spec-Driven | Issue #106 → approved spec (clarified 2026-11-09) → this plan; living contracts amended before code (`integration-events.md`, `signalr-hub.md`, `rest-api.md`, `data-model.md`) | ✅ |
| II. Modular monolith / slices | Modules interact only via `Contracts/` (events now live in `Contracts/IntegrationEvents/`); no module writes another's data afterwards (FR-081); `ModuleBoundaryTests` extended | ✅ |
| III. Test-first | Block tests, flow integration tests, anonymity contract test, latency test written first and failing | ✅ |
| IV. Blind anonymity | Payloads are ids only; Judging-consumed events asserted free of entrant fields | ✅ |
| V. Idempotency / immutability / state machine | Inbox gives exactly-once effects; no change to evaluation idempotency or the state machine; corrections stay audit-logged | ✅ |
| VI. Contract-first | New realtime event and integration events documented before code; REST response shapes unchanged | ✅ |
| VII. Security | No new endpoint; no secrets; logs never include payloads | ✅ |
| VIII. Accessibility | Banner uses `role="alert"` and is keyboard-dismissable; organizer screen (desktop-first) | ✅ |
| IX. Performance / offline | ≤ 1 s reaction; judge offline flows untouched | ✅ |
| X. Docs as part of done | ADR-0042 (outbox vs broker, transport abstraction), living doc (debt items closed, new debt: refresh race), CLAUDE.md layout (new block) | ✅ |
| Stack | No new dependency; MassTransit/Wolverine/CAP rejected (R-106-1) | ✅ |

Post-design re-check: still all ✅. No complexity-tracking entries. The extra block is the issue's
explicit learning goal and is not counted as accidental complexity.

## Project Structure

### Documentation (this feature)

```text
specs/106-integration-events-outbox/
├── spec.md
├── plan.md            # this file
├── research.md        # R-106-1 … R-106-13
├── data-model.md      # pointers to living contracts + outbox state machine
├── quickstart.md      # validation scenarios
├── checklists/requirements.md
└── tasks.md           # /speckit-tasks
```

Contracts are amended in place in `specs/001-birrapoint-mvp/contracts/` (new
`integration-events.md`; `signalr-hub.md`, `rest-api.md`) and `data-model.md`. No feature-local
`contracts/`.

### Source code touched

```text
src/BuildingBlocks/BuildingBlocks.Messaging/                 # NEW block (no BirraPoint reference)
├── IntegrationEventAttribute.cs                             # [IntegrationEvent(type, version)]
├── IIntegrationEventPublisher.cs / IntegrationEventPublisher.cs   # stage outbox row on caller's context
├── IIntegrationEventHandler.cs / IntegrationEventContext.cs # HandleAsync + OnCommitted callbacks
├── IIntegrationEventTransport.cs / LocalTransport.cs        # transport abstraction (#108 adds Dapr)
├── OutboxMessage.cs / InboxMessage.cs                       # shared-type entities
├── MessagingModelBuilderExtensions.cs                       # ApplyOutbox(name, schema) / ApplyInbox(name, schema)
├── MessagingServiceCollectionExtensions.cs                  # AddMessaging<TContext>(section, m => m.Publishes<>/Subscribe<,>)
├── OutboxDispatcher.cs / OutboxSql.cs / RetryPolicy.cs      # claim/lease/backoff/dead, model-derived SQL
├── RetentionPurger.cs                                       # FR-090 batched purge, run from the dispatcher sweep
├── InboxRunner.cs                                           # scope + tx + inbox insert + handler + commit + callbacks
├── OutboxWakeUpInterceptor.cs / OutboxWakeUp.cs             # signal after commit
├── MessagingOptions.cs / MessagingLog.cs
src/BirraPoint.Api/
├── Program.cs                                               # AddMessaging, interceptor on AddDbContext
├── Common/Persistence/AppDbContext.cs                       # ApplyOutbox/ApplyInbox per module schema
├── Common/Persistence/LegacyDatabaseGuard.cs                # FR-091 / R-106-12 fix (newer ids allowed)
├── Common/Persistence/Migrations/*_AddIntegrationMessaging  # NEW migration
├── Api/Domain/DispatchJobType.cs                            # + RefreshResults
├── Modules/Organization/
│   ├── Contracts/Events/                                    # DELETED (MediatR notifications)
│   ├── Contracts/IntegrationEvents/CompetitionFinalized.cs, JudgeEmailChanged.cs   # NEW
│   ├── Integration/UpdateBosFlagsOnTableJudgesChanged.cs    # becomes IIntegrationEventHandler, recompute
│   ├── Integration/BosFlagRecalculator.cs                   # NEW shared recompute (R-106-7)
│   ├── Features/Competitions/Features/ChangeCompetitionState/ChangeCompetitionStateCommand.cs  # publish event, drop wake-up
│   ├── Features/Judges/Features/UpdateJudgeEmail/UpdateJudgeEmailCommand.cs                  # recompute + publish
│   └── OrganizationModule.cs                                # messaging registrations
├── Modules/Judging/
│   ├── Contracts/IntegrationEvents/TableJudgesChanged.cs, EvaluationCorrected.cs          # NEW
│   ├── (Organization/Contracts/IJudgeEngagement.cs + GetEngagedJudgesAsync: new read, implemented here)
│   ├── Integration/JudgeEngagement.cs                       # implement new read
│   ├── Integration/AlertOrganizersOnJudgeConflict.cs        # NEW handler (JudgeConflictDetected)
│   ├── Features/Tables/CommonComponents/TableAssignmentApplier.cs   # publish integration event
│   ├── Features/Evaluations/Features/CorrectEvaluation/CorrectEvaluationCommand.cs  # publish when Finalized
│   └── JudgingModule.cs
├── Modules/Publication/
│   ├── Features/Dispatch/Features/DispatchScoreSheet/StageGeneratePdfsOnCompetitionFinalized.cs  # handler + wake after commit
│   ├── Features/Dispatch/Features/RefreshResults/          # NEW: StageRefreshOnEvaluationCorrected, RefreshResultsHandler
│   ├── Features/Dispatch/CommonComponents/ResultsArchiveBuilder.cs  # extracted from BundleZipHandler
│   └── PublicationModule.cs
src/Birrapoint.Web/src/app/
├── core/realtime/competition-hub.events.ts                  # + JudgeConflictDetected
├── features/dashboard/competition-monitor.component.ts      # banner
└── features/<tables screen>                                  # banner (same shared component)
tests/
├── BuildingBlocks/BuildingBlocks.Messaging.Tests/           # NEW (unit + Testcontainers)
├── BirraPoint.Api.UnitTests/Persistence/LegacyDatabaseGuardTests.cs   # + newer-id case
├── BirraPoint.Api.UnitTests/... ModuleBoundaryTests          # events only via Contracts/IntegrationEvents
└── BirraPoint.Api.IntegrationTests/IntegrationEvents/       # NEW: per-flow, crash/redelivery, latency, anonymity
    + updates: CompetitionFinalizationAtomicityTests, Create/UpdateTableApiTests, TableAssignmentApplierTests (wait for effects)
```

**Structure Decision**: The new reusable block follows ADR-0035/0037. Events are public module
contracts in `Contracts/IntegrationEvents/`, and handlers live in the subscriber's `Integration/`
(or in a slice when they start that slice's job), as cross-module wiring does today.

### Agent ownership

| Part | Agent |
|---|---|
| Messaging block + tests, host wiring, migration, guard fix, module publishers/handlers, `RefreshResults` | backend-engineer |
| Flow integration tests, crash/redelivery, latency, anonymity contract test, existing-test waits | qa-engineer (tests first) |
| `JudgeConflictDetected` banner + Jest | frontend-engineer |
| ADR-0042, living doc, CLAUDE.md layout | docs-keeper |

## Complexity Tracking

No constitution violations.
