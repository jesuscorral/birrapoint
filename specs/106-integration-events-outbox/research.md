# Research: Integration events between modules with a transactional outbox

Decisions for [spec.md](./spec.md) (FR-076–FR-089). Each entry: decision, rationale, alternatives.

## Current state (inventory for FR-081)

Cross-module writes inside one transaction today are MediatR `INotification`s published before
`SaveChangesAsync`, handled on the shared `AppDbContext`:

| Event (today) | Publisher | Subscriber | Effect in the subscriber's data |
|---|---|---|---|
| `CompetitionFinalized` | Organization `ChangeCompetitionStateCommand` | Publication `StageGeneratePdfsOnCompetitionFinalized` | stages the `GeneratePdfs` job |
| `TableJudgesChanged` | Judging `TableAssignmentApplier` (create/update table) | Organization `UpdateBosFlagsOnTableJudgesChanged` | sets `BeerEntries.NotValidForBos` (FR-018) |

No other handler writes another module's tables (DbSet usage per module checked: Organization,
Judging, Publication and Catalog only write their own sets; the job table and audit log are
building blocks). Cross-module FK cascades exist (e.g. `TastingTables → Competitions`) but no
feature deletes a parent across modules; removing those FKs is #107. Synchronous cross-module
reads through `Contracts/` (e.g. `IConflictOfInterestChecker` for FR-017 rejection,
`ICompetitionFinalizationGuard`) stay as they are.

## R-106-1 — New block `BuildingBlocks.Messaging`, own code, Jobs pattern copied

**Decision**: New project `src/BuildingBlocks/BuildingBlocks.Messaging` (tests in
`tests/BuildingBlocks/BuildingBlocks.Messaging.Tests`, Testcontainers like the Jobs block). It
re-implements the Jobs block's claim/lease/backoff pattern (`FOR UPDATE SKIP LOCKED`, lease owner +
expiry, renew at lease/3, exponential backoff capped, expired-lease recovery, wake-up channel plus
safety-net poll) for its own outbox table. No reference to `BuildingBlocks.Jobs` and none to
BirraPoint (FR-087).

**Rationale**: The issue asks to reuse the *pattern*. Storing deliveries as jobs would put event
state in the `jobs` schema instead of the publisher's outbox, and tie the messaging block to the job
block's table and handler model. A separate block keeps the transport abstraction clean for #108.

**Alternatives**: (a) fan out events into `DispatchJobs` rows — rejected: outbox not owned by the
module, couples two blocks. (b) A library (MassTransit, Wolverine, CAP) — rejected: new heavy
dependency against the pinned stack and the learning goal; MassTransit is also commercial from v9.

## R-106-2 — One outbox and one inbox table per module schema, shared-type entities

**Decision**: The block exposes `OutboxMessage` and `InboxMessage` entities mapped as EF Core
*shared-type* entity types, one per module, via
`modelBuilder.ApplyOutbox(name: "Organization", schema: "organization")` and
`modelBuilder.ApplyInbox(name: "Publication", schema: "publication")`. Tables:
`<schema>.OutboxMessages`, `<schema>.InboxMessages`. Publishers: Organization, Judging.
Subscribers: Publication, Organization, Judging. Raw SQL reads table/column names from the model
(same approach as `JobSql`).

**Rationale**: FR-077 wants the event in the *publishing module's* outbox. With one shared
`AppDbContext` (until #107) the same CLR type must map to several tables: shared-type entities do
exactly that. When #107 gives each module its own context, each context maps one outbox and one
inbox, with no change to the block.

**Alternatives**: one `messaging.OutboxMessages` table with a `Module` column — rejected: contradicts
FR-077 and would need a data move in #107. One CLR type per module — rejected: boilerplate in
every module.

## R-106-3 — Event declaration and envelope

**Decision**: Integration events are `sealed record`s in the publisher's
`Modules/<M>/Contracts/IntegrationEvents/`, annotated `[IntegrationEvent("<module>.<kebab-name>",
version)]` (e.g. `organization.competition-finalized`, `1`). The outbox row is the envelope: `Id`
(v7 Guid = event id), `Type`, `Version`, `OccurredAt`, `PayloadJson` (`jsonb`), plus delivery
state. The current `Contracts/Events/` folder (MediatR notifications) is removed. Payload shape
changes are additive within a version; a breaking change is a new version handled side by side.

**Rationale**: FR-076 (id, type, version, occurred at, payload). A stable string type decouples
storage from CLR names, which matters once a broker (#108) carries them.

## R-106-4 — Publishing: stage on the change tracker, wake after commit

**Decision**: `IIntegrationEventPublisher.Publish<TEvent>(TEvent evt)` only adds an outbox row to
the caller's `DbContext` change tracker (like `IJobQueue.Stage`); it commits or rolls back with the
caller's own `SaveChangesAsync`/transaction (FR-077). Which outbox an event goes to is fixed at
registration (`messaging.Publishes<CompetitionFinalized>(outbox: "Organization")`). An EF
interceptor in the block (`SaveChangesInterceptor` + `DbTransactionInterceptor`) signals the
dispatcher right after the commit that included outbox rows: after `SavedChanges` when no explicit
transaction is open, else after `TransactionCommitted`. A safety-net poll (default 30 s) covers a
missed signal and crash recovery.

**Rationale**: Callers cannot forget the wake-up, and the signal never fires for a rolled-back
transaction. The wake-up is what keeps reactions under 1 s (FR-089).

**Alternatives**: callers signal by hand (as with `IJobWakeUp` today) — rejected: easy to forget,
and `ChangeCompetitionStateCommand` already shows the ceremony. Postgres `LISTEN/NOTIFY` — rejected
for now: needed only across replicas, and there is a single API replica.

## R-106-5 — Dispatcher and transport abstraction

**Decision**: A hosted `OutboxDispatcher<TContext>` sweeps every registered outbox: claims a batch
of due rows (`Pending` with `NextAttemptAt <= now`, or `Delivering` with an expired lease) with
`FOR UPDATE SKIP LOCKED`, in `OccurredAt` order, and hands each envelope to
`IIntegrationEventTransport.SendAsync(envelope)`. The only transport here is the in-process
`LocalTransport`: it runs every subscriber of that type (see R-106-6) and succeeds only if all
succeed. Outcome: `Delivered`; or a retry with backoff (`Attempts`, `NextAttemptAt`, `LastError`);
or `Dead` after `MaxAttempts`, logged at error level with type, event id and the failing subscribers
(no payload, FR-080). Dead rows are never claimed again, so they block nothing. Unknown types or versions
are `Deferred` (transport returns `DeliveryOutcome` `Delivered`/`Deferred`): the row stays `Pending`, is
retried every `MaxBackoff` without using attempts, and logs a warning. *Amended after PR review
(was: straight to `Dead`).* Defaults follow the Jobs block: lease 2 min, 5 attempts, backoff 1 s doubling
up to 60 s. Settings come from the `Messaging` section.

**Rationale**: FR-078/FR-080/FR-086. For #108 a Dapr transport publishes to a topic and is
"successful" once the broker accepts the message; the subscriber side runs the same inbox runner
from a Dapr subscription endpoint. Publishers and handlers do not change.

**Alternatives**: per-subscriber delivery rows — rejected for the DB transport: the inbox already
makes re-running a successful subscriber a no-op, so one state per event is enough. The broker
gives per-subscriber state natively in #108.

## R-106-6 — Subscribers: inbox runner, own transaction, after-commit callbacks

**Decision**: A subscriber implements `IIntegrationEventHandler<TEvent>`:
`HandleAsync(TEvent evt, IntegrationEventContext context, CancellationToken)`. It is registered
with its inbox (`messaging.Subscribe<CompetitionFinalized, StageResultsOnCompetitionFinalized>(inbox:
"Publication")`). For each delivery the runner opens a new DI scope and a transaction on the
context, inserts the inbox row (`EventId`, `Handler`) with `ON CONFLICT DO NOTHING`, skips the
handler when no row was inserted, otherwise calls it, saves and commits, then runs callbacks the
handler registered with `context.OnCommitted(...)` (realtime emits, job wake-up; FR-084). Handler
exceptions roll back the inbox row too, so the retry runs the handler again.

**Rationale**: FR-079: the effect and its "processed" mark commit together, so a crash between them
cannot double the effect. `Handler` in the inbox key lets one module run several handlers for the
same event.

## R-106-7 — Converging reactions (order-insensitive)

**Decision**: Payloads carry identifiers, not snapshots, and handlers recompute from the current
state.
- `TableJudgesChanged { CompetitionId, TableId, AddedJudgeIds, RemovedJudgeIds }`. Organization
  takes the entries owned by added ∪ removed judges and sets each entry's `NotValidForBos` to "some
  co-owner judge is currently engaged in the competition" (seated at an active seat, or has
  submitted an evaluation). It reads engagement through a new Judging contract method
  `IJudgeEngagement.GetEngagedJudgesAsync(competitionId, judgeIds)`.
- The same recompute (`BosFlagRecalculator`, in Organization `Integration/`) is reused by
  `UpdateJudgeEmail` in its own transaction, so no event is needed for the flags.
- The table-assignment response keeps computing `bosFlaggedEntryIds` synchronously from the
  ownership read, so the API contract does not change.

**Rationale**: No global order (spec Assumptions). Two quick table edits delivered in reverse order
still give the right flags.

## R-106-8 — `JudgeEmailChanged` (FR-082)

**Decision**: `UpdateJudgeEmailCommand` (Organization):
1. Collects the entries owned by the judge before and after the change.
2. Applies the BOS recompute (R-106-7) to their union in the same transaction.
3. Publishes `JudgeEmailChanged { CompetitionId, JudgeId }`, with no email in the payload.

Judging handles it (`AlertOrganizersOnJudgeConflict`): it finds the judge's active seats, asks
`IConflictOfInterestChecker` which entries the judge now owns, and intersects them with each
table's samples. For each table with a conflict it calls
`context.OnCommitted(() => PublishToOrganizersAsync(competitionId, "JudgeConflictDetected", { tableId, judgeId, judgeDisplayName, beerEntryIds }))`.
It writes nothing, and the inbox prevents a second alert. The PWA shows the alert as a banner in
the organizer competition monitor and on the tables screen.

**Rationale**: Flags are Organization data and change in Organization's own transaction, so no
cross-module write is needed. The conflict check needs Judging's seats, so it reacts to the event
and alerts (clarification 2026-11-09). Judging receives only ids (FR-085).

## R-106-9 — `EvaluationCorrected` (FR-083)

**Decision**: `CorrectEvaluationCommand` (Judging) publishes `EvaluationCorrected { CompetitionId,
BeerEntryId, EvaluationId }` when the competition is `Finalized` (read through
`ICompetitionAccess`). Publication handles it by staging a new job `RefreshResults { BeerEntryId }`
(new `DispatchJobType.RefreshResults`) and waking the worker after commit. The job:
- regenerates that entry's PDF when a sheet already exists;
- rebuilds the ZIP when an archive already exists, using the ZIP builder extracted from
  `BundleZipHandler` into Dispatch `CommonComponents/`;
- never enqueues `SendResultEmail`.

If the results pipeline has not produced the sheet or the archive yet, the pending
`GeneratePdfs`/`BundleZip` jobs read the corrected scores anyway.

**Rationale**: No email is re-sent (clarification). A job, not inline work, because PDF rendering is
slow and the job queue already retries.

**Race handling** (*amended after PR review; was accepted as known debt*): `RefreshResults` throws
`JobDeferredException` (generic deferral in `BuildingBlocks.Jobs`: back to `Pending` after a delay, no
attempt used) while the competition has a `Pending`/`Running` `GeneratePdfs` or `BundleZip` job, so it
never runs before the first generation ends.

## R-106-10 — Realtime after commit (FR-084)

**Decision**: Publishers keep their emits after `SaveChangesAsync`/commit, as today. Subscribers
emit only through `context.OnCommitted`. `ChangeCompetitionStateCommand` drops its manual
`IJobWakeUp.Signal()`: Publication's handler wakes the job worker after its own commit.

## R-106-11 — Latency target and test strategy

**Decision**: The wake-up path (R-106-4) gives sub-second delivery. An integration test measures
commit-to-effect for the table-judges flow 20 times with the safety-net poll set to 1 h (so only
the wake-up can deliver), and asserts p95 ≤ 1 s (SC-020). Existing integration tests that read a
cross-module effect right after the request (BOS flags in `CreateTable`/`UpdateTable` tests,
`CompetitionFinalizationAtomicityTests`) wait for it with a polling helper (≤ 5 s timeout), as
SC-023 allows. The finalize-atomicity test becomes: rollback ⇒ no outbox row; commit ⇒ outbox row
in the same transaction, and the job appears afterwards.

## R-106-12 — Legacy-guard fix is a prerequisite (FR-091)

**Decision**: Before adding this feature's migration, fix the guard finding left open on PR #111
(M1): `LegacyDatabaseGuard.Evaluate` refuses only unknown applied ids that sort before the baseline
id, and gets a unit test for "baseline + a newer id ⇒ allowed".

**Rationale**: This feature adds the first migration after the baseline. Without the fix, rolling
back to an older image crash-loops (`MigrateOnStartup`), and checking out an older branch asks for a
data-wiping reset.

## R-106-13 — Retention purge (FR-090)

**Decision**: The dispatcher runs a purge pass at most once per `PurgeInterval` (default 1 h), piggybacked on its sweep so it adds no extra wake-ups (Neon scale-to-zero). Per registered outbox: delete `Delivered` rows with `OccurredAt` older than `OutboxRetention` (default 7 d) and `Dead` rows older than `DeadRetention` (default 30 d, kept longer for diagnosis). Per registered inbox: delete rows with `ProcessedAt` older than `InboxRetention` (default 30 d; *amended after PR review, was 14 d*). Deletes go in batches of `PurgeBatchSize` (default 1000; `DELETE … WHERE "Id" IN (SELECT … LIMIT n)`) until a batch removes fewer than `n` rows. Each pass logs the counts per table (information level). `MessagingOptions.Validate` requires `InboxRetention > OutboxRetention`, `InboxRetention >= DeadRetention` and `InboxRetention > MaxBackoff × MaxAttempts + LeaseDuration`, so an event can never be redelivered after its inbox row is gone. `Pending`/`Delivering` rows are never touched.

**Rationale**: Bounded growth with no new hosted service and no new dependency. Batching keeps locks short, and the purge never competes with the claim query (different rows, `SKIP LOCKED` on the claim side).

**Alternatives**: Postgres partitioning by month with `DROP PARTITION` — rejected: overkill for a few rows per competition, and EF migrations get awkward. A separate cron job (`pg_cron`) — rejected: not available on every host (Neon free tier), and it hides the rule outside the block.
