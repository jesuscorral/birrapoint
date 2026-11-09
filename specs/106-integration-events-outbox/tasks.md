# Tasks: Integration events between modules with a transactional outbox

**Input**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [quickstart.md](./quickstart.md); living contracts [integration-events.md](../001-birrapoint-mvp/contracts/integration-events.md), [signalr-hub.md](../001-birrapoint-mvp/contracts/signalr-hub.md), [rest-api.md](../001-birrapoint-mvp/contracts/rest-api.md), [data-model.md](../001-birrapoint-mvp/data-model.md). Issue #106.
**Tests**: mandatory and first (constitution Principle III). Each test task is verified failing before its implementation task.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- Task ids stay in this file, commits and the PR. They never go in code, comments or test names; cite FR-/SC-/US/ADR or the reason instead.
- Paths: block `src/BuildingBlocks/BuildingBlocks.Messaging/` (= `BB.M/`), block tests `tests/BuildingBlocks/BuildingBlocks.Messaging.Tests/` (= `BB.M.Tests/`), API `src/BirraPoint.Api/` (= `Api/`), integration tests `tests/BirraPoint.Api.IntegrationTests/` (= `IT/`), unit tests `tests/BirraPoint.Api.UnitTests/` (= `UT/`).

## Phase 1: Setup and prerequisite (legacy-guard fix, FR-091)

**Goal**: the database may be ahead of the build before this feature adds its migration (R-106-12, the M1 finding left open on PR #111).

- [ ] T001 Unit tests in `UT/Common/Persistence/LegacyDatabaseGuardTests.cs`: applied = known baseline + an unknown id that sorts after the baseline ⇒ allowed (`null`); applied = an unknown id that sorts before the baseline ⇒ still refused; the existing cases (empty, baseline only, `public` history) are unchanged (FR-091, FR-070)
- [ ] T002 Fix `Api/Common/Persistence/LegacyDatabaseGuard.cs` `Evaluate`: refuse only unknown applied ids that compare ordinally lower than the earliest known migration id; update the doc comment to say why newer ids are accepted (rollback, older branch)
- [ ] T003 [P] Create the block project `BB.M/BuildingBlocks.Messaging.csproj` (net10.0, `Microsoft.AspNetCore.App` framework reference, EF Core Relational + Npgsql, `InternalsVisibleTo` tests), the test project `BB.M.Tests/BuildingBlocks.Messaging.Tests.csproj` (xUnit, Testcontainers PostgreSQL, same shape as `BuildingBlocks.Jobs.Tests`, incl. a `TestHost`), and add both to `BirraPoint.sln`; reference the block from `Api/BirraPoint.Api.csproj`
- [ ] T004 [P] Block independence test `BB.M.Tests/MessagingIndependenceTests.cs`: the block assembly references no `BirraPoint.*` and no other `BuildingBlocks.*` assembly (FR-087, same as `JobsIndependenceTests`)

## Phase 2: Foundational: the messaging block (blocks every story)

**Goal**: publish → outbox → dispatcher → transport → inbox runner works end to end on a test host, with retry/dead, wake-up and retention.
**Independent test**: `dotnet test tests/BuildingBlocks/BuildingBlocks.Messaging.Tests` green.

### Tests first

- [ ] T005 [P] `BB.M.Tests/RetryPolicyTests.cs`: attempts below the limit retry, at the limit go dead; backoff 1 s, 2 s, 4 s… capped at `MaxBackoff` (FR-080)
- [ ] T006 [P] `BB.M.Tests/MessagingOptionsTests.cs`: defaults (lease 2 min, safety-net poll 30 s, 5 attempts, max backoff 60 s, `OutboxRetention` 7 d, `DeadRetention` 30 d, `InboxRetention` 14 d, `PurgeInterval` 1 h, `PurgeBatchSize` 1000); validation fails for intervals < 1 s, attempts < 1, batch size < 1, `InboxRetention <= OutboxRetention`, and `InboxRetention <= MaxBackoff × MaxAttempts + LeaseDuration` (FR-090)
- [ ] T007 [P] `BB.M.Tests/EnvelopeTests.cs`: an event record with `[IntegrationEvent("x.thing-happened", 1)]` serializes to `Type`/`Version`/`PayloadJson` and back; a record without the attribute is rejected at registration; two events with the same type+version are rejected (FR-076)
- [ ] T008 [P] `BB.M.Tests/MessagingModelTests.cs`: `ApplyOutbox("A", "a")` and `ApplyInbox("B", "b")` map shared-type entities to `a."OutboxMessages"` / `b."InboxMessages"` with the columns, keys (`Id`; `EventId`+`Handler`) and indexes of the living data-model; two outboxes on the same model coexist (R-106-2)
- [ ] T009 [P] `BB.M.Tests/PublisherTests.cs` (Testcontainers): `Publish` adds an outbox row to the caller's change tracker without saving; it commits with the caller's `SaveChangesAsync`; a rolled-back explicit transaction leaves no row; an event goes to the outbox registered for its type; publishing an unregistered type throws (FR-077)
- [ ] T010 [P] `BB.M.Tests/DispatcherTests.cs` (Testcontainers): a committed event reaches every subscriber; the row becomes `Delivered`; two dispatcher instances never deliver the same row concurrently (`SKIP LOCKED`); a `Delivering` row with an expired lease is re-claimed after a simulated crash; due rows are claimed in `OccurredAt` order (FR-078)
- [ ] T011 [P] `BB.M.Tests/InboxRunnerTests.cs` (Testcontainers): the handler's effect and the inbox row commit together; redelivering the same event 3 times changes nothing; a handler that throws rolls back its inbox row and effect; with two subscribers where one fails, the retry skips the one that succeeded; `OnCommitted` callbacks run only after commit and never after a rollback (FR-079, FR-084, SC-019)
- [ ] T012 [P] `BB.M.Tests/DeadLetterTests.cs` (Testcontainers + captured logger): an always-failing subscriber is retried `MaxAttempts` times with growing `NextAttemptAt`, then `Dead` with one error log carrying type, event id and subscriber and no payload text; an unknown type goes straight to `Dead`; events published after a dead one are still delivered (FR-080, SC-022)
- [ ] T013 [P] `BB.M.Tests/WakeUpTests.cs` (Testcontainers, safety-net poll 1 h): a commit containing outbox rows wakes the dispatcher (delivery well under 1 s) both with `SaveChangesAsync` alone and inside an explicit transaction; a rolled-back transaction sends no signal; a save with no outbox rows sends none (FR-089, R-106-4)
- [ ] T014 [P] `BB.M.Tests/RetentionPurgeTests.cs` (Testcontainers + fake `TimeProvider`): one purge pass removes `Delivered` rows past `OutboxRetention`, `Dead` rows past `DeadRetention` and inbox rows past `InboxRetention`; it keeps younger rows and every `Pending`/`Delivering` row; batch size 2 with 5 old rows still removes all 5; the pass logs counts per table; a second pass within `PurgeInterval` does nothing (FR-090, SC-024)
- [ ] T015 [P] `BB.M.Tests/TransportAbstractionTests.cs`: a fake `IIntegrationEventTransport` registered instead of the local one receives the envelopes and publishers/handlers need no change (FR-086)

### Implementation

- [ ] T016 `BB.M/IntegrationEventAttribute.cs`, `OutboxMessage.cs` (+ `OutboxStatus`), `InboxMessage.cs`, `IntegrationEventEnvelope.cs`, `MessagingOptions.cs` (+ validator), `RetryPolicy.cs` (pass T005–T007)
- [ ] T017 `BB.M/MessagingModelBuilderExtensions.cs`: `ApplyOutbox(name, schema)` / `ApplyInbox(name, schema)` as shared-type entities; `OutboxSql.cs` reads quoted table/column names from the model, as `JobSql` does (pass T008)
- [ ] T018 `BB.M/IIntegrationEventPublisher.cs` + `IntegrationEventPublisher<TContext>.cs` and the registry (`MessagingBuilder.Publishes<TEvent>(outbox)` / `Subscribe<TEvent, THandler>(inbox)`) in `MessagingServiceCollectionExtensions.cs` `AddMessaging<TContext>(section, configure)` (pass T007, T009)
- [ ] T019 `BB.M/OutboxWakeUp.cs` + `OutboxWakeUpInterceptor.cs` (`SaveChangesInterceptor` + `DbTransactionInterceptor`), exposed for the host's `AddDbContext` (`AddMessagingInterceptors(sp)`) (pass T013)
- [ ] T020 `BB.M/IIntegrationEventHandler.cs`, `IntegrationEventContext.cs` (`EventId`, `OccurredAt`, `OnCommitted`), `InboxRunner<TContext>.cs` (scope, transaction, `INSERT … ON CONFLICT DO NOTHING`, handler, commit, callbacks) (pass T011)
- [ ] T021 `BB.M/IIntegrationEventTransport.cs` + `LocalTransport.cs` (runs every subscriber of the type via `InboxRunner`; fails if any fails, naming the failing handlers) (pass T015)
- [ ] T022 `BB.M/OutboxDispatcher<TContext>.cs` + `MessagingLog.cs`: hosted sweep over every registered outbox: claim with lease, renew at lease/3, deliver, mark `Delivered`/retry/`Dead`, expired-lease recovery, wake-up channel plus safety-net poll (pass T010, T012)
- [ ] T023 `BB.M/RetentionPurger.cs`, run from the dispatcher sweep at most once per `PurgeInterval`: batched deletes per table, counts logged (pass T014)

**Checkpoint**: block suite green; `dotnet build` clean.

## Phase 3: Host wiring (blocks the stories)

- [ ] T024 [P] Extend `UT/Architecture/ModuleBoundaryTests.cs`: integration events live only in `Modules/<M>/Contracts/IntegrationEvents/`; no module references another module's `INotification`s (the `Contracts/Events/` folder is gone); subscribers reference only the publisher's `Contracts` (FR-081, SC-021)
- [ ] T025 [P] Integration test `IT/IntegrationEvents/MessagingSchemaTests.cs`: after migrating, `organization`/`judging` have `OutboxMessages` + `InboxMessages`, `publication` has `InboxMessages`, nothing else changed (extend the schema map assertion in `IT/Persistence/SchemaTests.cs`)
- [ ] T026 Wire the host: `Api/Program.cs` `AddMessaging<AppDbContext>(Configuration.GetSection("Messaging"), …)` and the interceptor on `AddDbContext`; `Api/Common/Persistence/AppDbContext.cs` `ApplyOutbox`/`ApplyInbox` for the five tables with `DbSchemas` names; `Messaging` defaults in `appsettings.json`; test-host overrides in the integration `ApiFactory` (short lease and poll where needed)
- [ ] T027 Migration `Api/Common/Persistence/Migrations/*_AddIntegrationMessaging` (only the five tables + indexes; `Down` drops them); review with the `migration-safety` skill (pass T025)

## Phase 4: User Story 18 — exactly-once cross-module effects (P1) 🎯 MVP

**Goal**: `competition-finalized` and `table-judges-changed` travel as integration events; no module writes another's data (FR-081).
**Independent test**: quickstart §1, §2, §6.

### Tests first

- [ ] T028 [P] [US18] `IT/IntegrationEvents/CompetitionFinalizedFlowTests.cs`: finalize ⇒ one `organization.OutboxMessages` row committed with the state change; with the dispatcher held, no `GeneratePdfs` job; after delivery exactly one job and one `publication.InboxMessages` row; forced redelivery ×3 ⇒ still one job; a failing finalize transaction ⇒ no outbox row, no job (US18 1–4, FR-077, SC-019). Replaces the in-transaction assertions in `IT/Modules/Organization/Integration/CompetitionFinalizationAtomicityTests.cs`
- [ ] T029 [P] [US18] `IT/IntegrationEvents/TableJudgesChangedFlowTests.cs`: adding a judge who owns an entry flags it after delivery; removing them clears it when no co-owner is engaged and keeps it when one is; two table edits delivered in reverse order converge to the same flags; redelivery is a no-op (US18 5, FR-018, FR-079)
- [ ] T030 [P] [US18] `IT/IntegrationEvents/IntegrationEventLatencyTests.cs`: safety-net poll 1 h; 20 table-judge changes; commit-to-flag p95 ≤ 1 s (SC-020, FR-089)
- [ ] T031 [P] [US18] Update `IT/Modules/Judging/Tables/Features/CreateTable/CreateTableApiTests.cs`, `…/UpdateTable/UpdateTableApiTests.cs`, `IT/Modules/Judging/Tables/CommonComponents/TableAssignmentApplierTests.cs`, `IT/Modules/Judging/Tables/TablesApiTestBase.cs`: response `bosFlaggedEntryIds` unchanged and asserted at once; the persisted `NotValidForBos` is awaited with a shared polling helper `IT/IntegrationEvents/EventualAssert.cs` (≤ 5 s) (SC-023)

### Implementation

- [ ] T032 [US18] Judging contract read: add `GetEngagedJudgesAsync(competitionId, judgeIds)` to `Api/Modules/Organization/Contracts/IJudgeEngagement.cs` and implement it in `Api/Modules/Judging/Integration/JudgeEngagement.cs` (active seat anywhere in the competition, or any submitted evaluation), with a test in its existing integration test file
- [ ] T033 [US18] `Api/Modules/Organization/Integration/BosFlagRecalculator.cs`: for given entries, set `NotValidForBos` = some co-owner judge currently engaged (R-106-7)
- [ ] T034 [US18] Events `Api/Modules/Organization/Contracts/IntegrationEvents/CompetitionFinalized.cs` and `Api/Modules/Judging/Contracts/IntegrationEvents/TableJudgesChanged.cs` (`[IntegrationEvent]`, payloads per integration-events.md); delete `Api/Modules/Organization/Contracts/Events/`
- [ ] T035 [US18] Publishers: `ChangeCompetitionStateCommand.cs` publishes `CompetitionFinalized` through `IIntegrationEventPublisher` and drops the MediatR publish and `IJobWakeUp`; `TableAssignmentApplier.cs` publishes `TableJudgesChanged` (ids only) and keeps computing `bosFlaggedEntryIds` for the response
- [ ] T036 [US18] Subscribers: `Api/Modules/Organization/Integration/UpdateBosFlagsOnTableJudgesChanged.cs` becomes an `IIntegrationEventHandler` that uses `BosFlagRecalculator` over the entries owned by the added and removed judges; `…/DispatchScoreSheet/StageGeneratePdfsOnCompetitionFinalized.cs` becomes a handler that stages `GeneratePdfs` and wakes the job worker through `context.OnCommitted`. Register publishes and subscriptions in `OrganizationModule.cs`, `JudgingModule.cs` and `PublicationModule.cs` (pass T028–T031)

**Checkpoint**: US18 tests and the whole integration suite green.

## Phase 5: User Story 19 — failed deliveries retry and end visibly (P2)

**Goal**: dead deliveries are visible in the host's logs and block nothing (the block behaviour is proven in T012).
**Independent test**: quickstart §3.

- [ ] T037 [P] [US19] `IT/IntegrationEvents/DeadLetterHostTests.cs`: a test-only subscriber registered in the host that always throws ⇒ after `MaxAttempts` the row is `Dead`, the error log names type/id/subscriber and contains no payload or email; a `competition-finalized` published afterwards is still delivered (FR-080, SC-022)
- [ ] T038 [US19] Make T037 pass through host configuration only (log category and level in `appsettings.json`, no block change expected); if a change is needed, return it to the block with a test

## Phase 6: User Story 20 — judge email correction re-checks conflicts (P2)

**Goal**: `judge-email-changed` closes the FR-017/FR-018 debt (FR-082).
**Independent test**: quickstart §4.

### Tests first

- [ ] T039 [P] [US20] `IT/IntegrationEvents/JudgeEmailChangedFlowTests.cs`:
  - a new email that owns entry E flags E in the same response transaction;
  - the old email's flag clears when no co-owner is engaged;
  - one outbox row is written;
  - when the judge sits at E's table, organizers get `JudgeConflictDetected { tableId, judgeId, judgeDisplayName, beerEntryIds }` after delivery (SignalR test client) and the judge stays seated;
  - no alert when there is no conflict;
  - redelivery sends no second alert.

  (US20 1–3, FR-082)
- [ ] T040 [P] [US20] `IT/IntegrationEvents/IntegrationEventAnonymityTests.cs`: serialized payloads of every event consumed by Judging (today `organization.judge-email-changed`), and of all four events, contain no entrant or personal field (name, participant, brewery, origin, email) (FR-085)
- [ ] T041 [P] [US20] Jest `src/Birrapoint.Web/src/app/shared/…/judge-conflict-banner.component.spec.ts`:
  - shows judge and table for a `JudgeConflictDetected` event;
  - has `role="alert"`;
  - dismisses with the keyboard;
  - one banner per table.

  `competition-hub.events.ts` gets the typed event

### Implementation

- [ ] T042 [US20] `Api/Modules/Organization/Contracts/IntegrationEvents/JudgeEmailChanged.cs`; `…/UpdateJudgeEmail/UpdateJudgeEmailCommand.cs` collects the entries owned before and after, runs `BosFlagRecalculator` on their union and publishes the event, all in its transaction
- [ ] T043 [US20] `Api/Modules/Judging/Integration/AlertOrganizersOnJudgeConflict.cs`: handler; active seats of the judge, entries owned via `IConflictOfInterestChecker`, intersect with table samples, `OnCommitted` emit `JudgeConflictDetected` to `competition:{id}:organizers`; register in `JudgingModule.cs` (pass T039, T040)
- [ ] T044 [US20] Frontend: `core/realtime/competition-hub.events.ts` + `JudgeConflictDetected`; shared `judge-conflict-banner` component shown in `features/dashboard/competition-monitor.component.ts` and the `features/table-management` screen; visual check in the browser (pass T041)

## Phase 7: User Story 21 — correction after finalizing refreshes results (P3)

**Goal**: `evaluation-corrected` closes the stale-results debt (FR-083).
**Independent test**: quickstart §5.

### Tests first

- [ ] T045 [P] [US21] `IT/IntegrationEvents/EvaluationCorrectedFlowTests.cs`:
  - a correction on a `Finalized` competition with results ⇒ one `RefreshResults` job;
  - the entry's PDF `GeneratedAt` and bytes change, and the ZIP is rebuilt with the corrected total;
  - no new `SendResultEmail` job;
  - a correction before `Finalized` ⇒ no event, no job;
  - when the sheet or archive does not exist yet, the job does nothing and completes;
  - redelivery ⇒ one job.

  (US21 1–3, FR-083)

### Implementation

- [ ] T046 [US21] `Api/Modules/Judging/Contracts/IntegrationEvents/EvaluationCorrected.cs`; `CorrectEvaluationCommand.cs` publishes it when `ICompetitionAccess` reports `Finalized`
- [ ] T047 [US21] Extract the ZIP building from `BundleZipHandler.cs` into `Api/Modules/Publication/Features/Dispatch/CommonComponents/ResultsArchiveBuilder.cs`; `BundleZipHandler` uses it with unchanged behaviour (existing dispatch tests stay green)
- [ ] T048 [US21] `DispatchJobType.RefreshResults` in `Api/Domain/DispatchJobType.cs`; slice `Api/Modules/Publication/Features/Dispatch/Features/RefreshResults/`: `StageRefreshOnEvaluationCorrected.cs` (handler, stages the job, wakes the worker on commit) and `RefreshResultsHandler.cs` (regenerate that PDF if a sheet exists, rebuild the ZIP if an archive exists, no emails); register in `PublicationModule.cs` (pass T045)

## Phase 8: Polish and cross-cutting

- [ ] T049 [P] Gates: `dotnet build BirraPoint.sln`, `dotnet format --verify-no-changes`, unit + integration + every block suite; `npx jest`, `npx ng lint`, `npm run format:check`, `npm run build:budget`; `node .claude/hooks/guard.test.js` unaffected
- [ ] T050 [P] Quickstart §2, §4, §5 and §8 manually on the local stack (Aspire); record results for the PR
- [ ] T051 [P] Grep that no `INotification`/`IPublisher.Publish` remains for cross-module writes (`git grep -n "INotification" -- src/BirraPoint.Api`) and that no task id leaked into code (`.github/scripts/check-no-task-ids.sh`)
- [ ] T052 docs-keeper (FR-088):
  - ADR `Docs/adrs/0042-integration-events-outbox.md` (outbox vs broker, transport abstraction, shared-type outbox per module, retention);
  - `Docs/arquitectura_viva.md`: the `UpdateJudgeEmail` and post-`Finalized` correction debt items are closed; new debt is the refresh race (R-106-9);
  - `CLAUDE.md` layout lists `BuildingBlocks.Messaging`;
  - check the amended living contracts for drift.

## Dependencies

- Phase 1 (T001–T004) → Phase 2 (block) → Phase 3 (host wiring) → stories.
- US18 (Phase 4) comes before US20 and US21: they reuse `BosFlagRecalculator`, the registrations and the polling helper. US19 needs only Phase 3.
- Within each phase, the tests come first and are verified failing; implementation tasks run in the order listed.
- T047 comes before T048. T042 comes before T043. T041 comes before T044.

## Parallel execution

- Phase 1: T003 ∥ T004 after T001→T002 (different areas).
- Phase 2 tests: T005–T015 all [P] (separate files). Implementation T016→T023 sequential (shared types).
- Stories: qa-engineer writes T028–T031, T037, T039–T040 and T045 together, while backend-engineer runs Phase 2/3. frontend-engineer runs T041/T044 at any time after the contract (already written).

## Implementation strategy

MVP = Phases 1–4: the guard fix, the block, the wiring and the two existing flows moved to events. That is mergeable on its own. Then US19 (config), US20 (closes COI debt + banner) and US21 (results refresh). Polish last.
