# Living Architecture — BirraPoint

> Living document: it reflects the **actual current state** of the system, and MUST be updated
> at the close of every backlog task (see CLAUDE.md §Implementation workflow, step 6).
> Decisions with trade-offs are recorded in `Docs/adrs/`; the approved design lives in
> `specs/001-birrapoint-mvp/`. All documentation in this repository is written in English.

**Last updated:** 2026-07-28 · after T089–T091, T094 — **Phase 15 (Polish & Cross-Cutting Concerns)
in progress**

## Global status

Phase 1 (Setup, T001–T007), Phase 2 (Foundational, T008–T020), Phase 3 (US1, Secure Access,
T021–T024), Phase 4 (US2, Competition Creation Wizard with Drafts, T025–T030), Phase 5 (US3, Beer
Entry Import with In-Flow Correction, T031–T037), Phase 6 (US4, Judge Registration and
Automatic Invitations, T038–T044), and Phase 7 (US5, Table Setup with Conflict-of-Interest
Protection, T045–T049) are **complete**. Phase 8 (User Story 6 — Blind Table Dynamics: Shared
Fixed Order, T050–T054) is also **complete**: the judge-facing `Features/TastingOrder/` slice
(`GET /me/tables`, `GET /me/tables/{tableId}/samples`, one-shot `POST /me/tables/{tableId}/order`
serialized via `SELECT ... FOR UPDATE`, T050–T052) plus the first real frontend content in
`features/judge-tables/` — blind sample list, CDK drag-drop + keyboard move-up/down reorder
(FR-020), a live `TableOrderFixed` subscription over `CompetitionHubService` (T053) — and an E2E
spec proving cross-session propagation within FR-021's ≤1s budget across two independent judge
browser sessions (T054). Quickstart scenarios 1–5 pass end to end; scenario 6 passes for the
order-fix/propagation/lock behavior this story delivers (the "sheets openable only in fixed
sequence" half needs the evaluation sheet, US7/T061, not built yet).

**Phase 17 (User Story 13 — Organizer Competition Selection, T100–T101)**, added and built the same
day after a UI-mockup review surfaced a real gap: the organizer landing page had no way to see or
pick a competition beyond a directly-typed URL. Backend-free — `GET /competitions` already existed
(T027) — so this was a pure frontend addition: `features/dashboard/` (the real
`OrganizerDashboardComponent`, replacing T024's placeholder) lists the caller's competitions with a
state badge and routes `Draft` into the wizard, `Active`+ into the tables screen (a stand-in until
Phase 11/US9 ships a unified management view). Found and fixed the same day: a pre-existing
app-shell bug (`app.html`, since T003) that pushed every route's content below the fold behind a
static full-viewport splash — see Recorded debt/Frontend section below.

**T102–T103 (2026-07-22)** closed both items PR #21's review left open, same day: (1) a senior-code-
reviewer FSD finding — `CompetitionsApiService` relocated from `features/competition-wizard/` to
`core/api/` now that a second feature (`features/dashboard/`) consumes it; (2) a real product gap —
FR-051/Acceptance Scenario 5 add an advance-state action to each dashboard row (`Draft`→"Activate"→
`Active`→"Start evaluation"→`InEvaluation`→"Finalize"→`Finalized`, one step at a time per FR-006,
behind an explicit irreversible-action confirm), calling the already-existing
`POST /competitions/{id}/state` (T028) for the first time from real UI. Two E2E specs
(`us6-order.spec.ts`, `us13-dashboard.spec.ts`) that had been working around the missing button
with a captured-bearer-token direct API call now drive the real button instead.

**Phase 15 (Polish & Cross-Cutting Concerns, T089–T094)** started 2026-07-28, after Phase 14 (US12)
merged (PR #27) with all 13 user stories functionally complete. **T089** (accessibility), **T090**
(k6 + SC-006 scale E2E), **T091** (bundle/perf budgets), and **T094** (security pass) are done — see
Testing & quality gates and Recorded debt below for exactly what each closed. **T092** (run all 13
quickstart scenarios, fix doc drift) and **T093** (this file, `README.md`, `CLAUDE.md`) are in
progress in this same change. T092's SC-010 usability gate ("≥5 first-time judges complete an
evaluation sheet unaided in under 10 minutes") is a human user study — no amount of automation
satisfies it; it remains an explicit outstanding manual/team action, not something this phase closes.

**Phase 9 (User Story 7 — Offline-First Validated Evaluation Sheet, T055–T061)** is now **complete**
(**correction**: an earlier revision of this line claimed this was "the last P1 story" — wrong,
Phase 10/US8 below was still pending at the time; fixed here). Backend: `Features/
Evaluations/` (`POST /me/tables/{tableId}/evaluations`, idempotent via a unique-constraint-catch on
the (judge, entry) index rather than a pre-check — the first genuine insert-time race guard in this
codebase, since locked-on-submit forbids ever pre-checking-then-upserting) and `Features/Catalog/
GetStyleDetail.cs` (`GET /styles/{code}`, FR-049). Frontend: `core/offline/sync.service.ts` is the
real offline engine T020 only scaffolded the Dexie tables for — drafts debounced ≤300ms, outbox
durable-first submit with capped-exponential-backoff replay on `window online` / service
construction / post-submit, deliberately not the Background Sync API (unsupported on iOS Safari,
R-08); `features/evaluation-sheet/` is the capped five-section sheet + collapsible BJCP style
reference panel; `judge-tables/judge-table-order.component.ts` gained the FR-022 sequential-gating
entry point ("Evaluate" only on the first `NotStarted` sample, "Locked" on the rest). An E2E spec
(`us7-offline.spec.ts`) proves the full offline round-trip and, along the way, found and fixed a
real bug: a genuine offline app restart fell back to a load-error screen instead of the cached
sample. See Recorded debt below for two things this phase left open on purpose.

**Phase 10 (User Story 8 — Table Closing and Score Immutability, T062–T067)** is now **complete —
this really is the last P1 story**, so every priority-1 user story in the MVP spec is built.
Backend: `CloseTable.cs` (`POST /me/tables/{tableId}/close`, JUDGE, any active member) gates on
completeness (every active judge × sample must have a submitted `Evaluation`, else
`409 evaluations-incomplete { missing: [blindCode] }`) and zero open `DiscrepancyAlert`s (else
`409 discrepancy-open { blindCodes }` — always empty at this point in the build, since discrepancy
detection didn't land until Phase 13/US11, see below), then flips
`TastingTable.State`/`ClosedAt` and emits **two different `TableClosed` payloads** — `{ tableId }`
to the judge group, `{ tableId, consolidatedScores: [{ blindCode, mean }] }` (FR-042) to the
organizer group — from one handler, matching contracts/signalr-hub.md exactly rather than
conflating the two audiences. `CorrectEvaluation.cs` (`PUT /competitions/{id}/evaluations/{
evaluationId}`, ORGANIZER-only, allowed regardless of table state) re-validates the same caps/
comment-length rules as a judge submission, lets `Evaluation.Total`'s computed column recompute
itself, and audits before/after via the existing `IAuditWriter` convention (FR-035). Both share
`CloseTableRules` (pure: missing-blind-code computation, mean averaging) — same "pure rule beside
DB-touching handler" split as `SubmitEvaluationRules`/`TastingOrderRules`. Immutability (FR-034)
needed no new backend guard at all — `SubmitEvaluation`'s existing `table.State != Open` → `409
table-closed` check (built in Phase 9) already covers "no further evaluations after close,
including late offline syncs"; `CloseTable`'s only job is to actually flip that flag. Frontend:
`judge-table-order.component.ts` gained a "Close table" action (visible only once every sample is
past `NotStarted` and the order is fixed) behind the same `alertdialog`+`cdkTrapFocus` confirm
pattern as "Fix order", a live `TableClosed` hub subscription so a different judge's close reflects
immediately, and handling for all three documented `409`s (a same-race `table-closed` resolves
silently to the closed state, not an error — the judge's desired outcome already happened, just not
by their own click). No organizer-facing UI exists yet for `CorrectEvaluation` — it's exercised
today only via direct API calls (E2E, and presumably real incident response) — a natural fit for
whatever the Phase 11 (US9) monitoring dashboard's audit drill-down eventually needs.

**Phase 11 (User Story 9 — Live Monitoring Dashboard with Audit, T068–T071)** is now **complete**
— the first P2 story built. Backend: `Features/Monitoring/GetProgress.cs` (`GET
/competitions/{id}/progress`, ORGANIZER-only) returns every table's `{ tableId, name, state,
completed, expected, percent }`, using the exact same completed/expected/percent formula
`SubmitEvaluation.cs`'s `EvaluationCompleted` emit already used, so a freshly-fetched row and a
live-patched one can never disagree. `GetEntryEvaluations.cs` (`GET
/competitions/{id}/entries/{entryId}/evaluations`) is the FR-038 audit drill-down — every judge's
scores/comments/total/status (reusing `SubmitEvaluation`'s `EvaluationScoresDto`/
`EvaluationCommentsDto`, no need to redefine them), plus a consolidated mean that's `null` until
the entry's table closes, then computed inline (a deliberate small duplication of
`CloseTableRules.ComputeMean`'s 2-decimal rounding rather than a cross-feature-folder import — this
codebase's established preference, see `CorrectEvaluationCommandValidator`'s similar duplication of
`SubmitEvaluationRules`). Both 404 on non-ownership or a non-existent/unassigned entry, never
leaking existence, same convention as `ListEntriesQuery`/`CorrectEvaluation`. Also closed a gap
open since T052: `Features/TastingOrder/FixOrder.cs` now emits `TableOrderFixed` to the organizer
group too, not just the judge group — the contract had documented this row since Phase 8 but
nothing emitted it. Frontend: `getEntries` (previously `table-management`-only) was promoted to
`core/api/entries-api.service.ts` — the same FSD "≥2 features → `core/api/`" rule already applied
to `CompetitionsApiService`/`CatalogApiService`. New `core/api/monitoring-api.service.ts` wraps both
endpoints. New `features/dashboard/competition-monitor.component.ts` (route
`organizer/competitions/:id/monitor`, now `OrganizerDashboardComponent`'s `destination()` target for
`InEvaluation`/`Finalized` — `Active` still goes to `tables`, there's nothing left to *set up* once
evaluation has started) loads the initial progress/entries/competition-header GETs once, then
patches state in place from three hub events on the `competition:{id}:organizers` group it now
joins for the first time (that join method existed since T020 but had no subscriber until this
phase): `EvaluationCompleted` replaces just the matching row's completed/expected/percent (no
refetch, satisfying FR-037's "no reload, no flicker"), `TableClosed` flips that row's badge to
`Closed`, `TableOrderFixed` shows a per-table "Order fixed by {name}." note. The audit drill-down
always fetches fresh via `getEntryEvaluations` on a sample click rather than caching `TableClosed`'s
`consolidatedScores` — one round trip per click, simpler than keeping a second cache in sync, and
renders everything as plain text/`<dl>` pairs with no form controls (FR-038's read-only
requirement, contract-tested in the E2E spec by asserting zero `input`/`textarea` inside the
drill-down). **Known, verified gap** (found by the E2E work, not by inspection): the "Order fixed
by" note has no REST backfill — `GetProgressQuery`'s response carries no order-fixed field at all,
so it's populated *purely* from the live `TableOrderFixed` event. An organizer who opens the
monitor screen *after* a table's order was already fixed will never see that note for that table
(fixing order is one-shot, so no second event will ever arrive to populate it retroactively) — not
a functional problem (the note is purely informational, nothing depends on it), but worth knowing
if a future task touches this area. See Recorded debt below.

**Phase 12 (User Story 10 — Event Closing with Automated Results Dispatch, T072–T078)** is now
**complete**. A real data-model gap surfaced during implementation: nothing in `plan.md`/
`data-model.md` ever said where generated PDF bytes or the bundled ZIP would live — this stack has
no blob/file storage at all (Postgres + IndexedDB only). Resolved by adding two entities
(`data-model.md` updated) storing them as `bytea` directly in Postgres: `GeneratedScoreSheet`
(one row per `BeerEntry`, upserted on regeneration) and `ResultsArchive` (one row per
`Competition`). Backend: `ChangeState.cs`'s existing `Finalized` transition (the `tables-still-open`
gate has existed since T028 but had zero test coverage until this phase — backfilled at the
integration level) now also enqueues one `GeneratePdfs` `DispatchJob` (FR-036's actual trigger).
Three new `IDispatchJobHandler`s chain by enqueueing the next stage on success —
`GeneratePdfsHandler` renders one `ScoreSheetDocument` (QuestPDF) per beer entry, reusing the same
join shape `GetEntryEvaluationsQueryHandler` (Phase 11) already established for judge names/
scores/consolidated mean, deliberately duplicated rather than cross-imported per this codebase's
established preference; `BundleZipHandler` assembles an in-memory `System.IO.Compression.ZipArchive`
at the FR-040 path (`{CompetitionName}/{ParticipantId}/{StyleCode}_{BlindCode}.pdf`, a pure
`DispatchPaths.ZipEntryPath` helper) and enqueues one `SendResultEmail` job **per participant** —
the same one-job-per-recipient convention `SendInvitation` already uses, which is also how FR-041's
per-recipient status/retry works: each participant's send is tracked entirely via that
`DispatchJob`'s own `Status`/`Attempts`/`LastError`, no separate email-status entity needed.
`SendResultEmailHandler` attaches every PDF belonging to that participant's entries via a new
`IEmailSender.SendWithAttachmentsAsync` (the pre-existing `SendAsync`/`SendInvitationHandler` are
untouched). Dispatch scope is **competition-wide, not table-wide** — every participant and every
beer entry in the competition gets a PDF/email/ZIP folder, including an entry that was never
assigned to any table and so has zero evaluations (confirmed by the E2E spec, which asserts on
exactly this case). Retry (`POST .../dispatch/retries`) resets a targeted job to a fresh
`Pending`/`Attempts: 0` rather than continuing an exhausted automatic backoff — picked up by
`DispatchWorker`'s existing 30s safety-net poll, no proactive wake-up needed. Frontend: the
"finalize" action needed no new UI at all — it's just another transition through the existing
T102 advance-state button; `features/results-dispatch/` is the screen reached *afterward*, linked
from the monitor screen once a competition is `Finalized`. Archive download had to be a `Blob`
fetch through `HttpClient` (not a plain `<a href>`) since only `HttpClient` requests carry the
bearer token via the auth interceptor; the component then drives the actual browser save with
`URL.createObjectURL` + a synthetic anchor click. Archive readiness has no cheap standalone check
in the contract (the archive endpoint IS the readiness check and the download in one call), so the
frontend infers it from two signals: an exact live `DispatchProgress` `{jobType: BundleZip, status:
Completed}` event, or — for a page loaded after the pipeline already finished — a documented
conservative fallback proxy (every participant status row reaching a terminal state).

**Scope note**: T048A's beer/judge detail modals ship without allergen/special-award badges or
judge BJCP-certification fields — a prior session's task-doc edit referenced them with zero
backing anywhere in spec.md/data-model.md/contracts; user-approved decision to scope them out
rather than invent product behavior no one specified. `UpdateJudgeEmail`'s COI/BOS re-run
(FR-017/018, deferred in Phase 6) is *still* not implemented — `Features/Tables` now exists, so
the blocking dependency is gone, but wiring it up was never in either phase's task list. Both
tracked in Recorded debt below.

**Scope note**: `UpdateJudgeEmail`'s COI/BOS re-run (FR-017/FR-018, contract text) is deliberately
NOT implemented yet — see Recorded debt below.

**Sequencing note**: this phase's backend and most of its frontend were actually built *before*
Phase 3 landed, on this same branch name (`feature/T025-T030`) — `tasks.md` explicitly allows split
dev streams since US2 has no story dependency on US1 (`Team split` note, §Parallel Opportunities).
That work sat uncommitted and was preserved via `git stash` while Phase 3 was implemented and
merged first; this update is that stashed work reconciled onto the post-Phase-3 `main` (mainly:
`app.routes.ts`'s wizard routes now nest under T024's `organizer` parent instead of predating its
route restructure) plus the one genuinely new piece, T030.

**Phase 13 (User Story 11 — Discrepancy Consensus Alert, T079–T083)** is now **complete**, closing
out the two forward-declared gaps Phase 9/10 left on purpose (`SubmitEvaluation`'s `Status` always
`Confirmed`, `CloseTable`'s `discrepancy-open` check vacuously empty). Backend: a new pure
`DiscrepancyRules.ComputeInvolvedJudgeIds` (FR-031) — a judge is "involved" iff their total differs
by **strictly more than 7** from at least one *other* judge's total for the same (table, sample),
not "the group's spread exceeds 7" — the distinction matters for 3+ judges, where a middle judge
within 7 of everyone can sit outside an alert two outliers are in. `Discrepancy.cs` holds everything
else: an internal `DiscrepancyReconciler.ReconcileAsync` (stages `Evaluation.Status` flips and
opens/resolves the single `DiscrepancyAlert` row per (table, entry) on the change tracker, no
`SaveChangesAsync` of its own — same "stage then let the caller save" split as `CorrectEvaluation`'s
audit write) shared by `SubmitEvaluation` (now actually calls it, in a second `SaveChangesAsync`
after the insert commits, since `Evaluation.Total` is a DB-computed column not known before that)
and the new `AdjustEvaluationCommand` (`PUT /me/tables/{tableId}/evaluations/{evaluationId}`,
JUDGE-only, Clarification Q2's only sheet-reopening path — 409 `evaluation-locked` unless an open
alert currently involves the caller). `GetMyDiscrepanciesQuery` (`GET
/me/tables/{tableId}/discrepancies`) filters to alerts the caller is currently part of, per
`DiscrepancyViewBuilder`. One deliberate per-judge distinction worth remembering: the response's
`discrepancy` field (on both submit and adjust) reflects **the acting judge's own involvement**, not
whether the entry has any open alert at all — a third judge landing cleanly between two already-
divergent judges gets `status: "Confirmed"`, `discrepancy: null` in their own response, even though
an alert stays open for that sample; the SignalR `DiscrepancyRaised`/`DiscrepancyResolved` events
(both audiences, per contracts/signalr-hub.md) are entry-level instead, firing whenever the
*global* involved set is non-empty/just emptied, regardless of which judge triggered the
reconciliation. `contracts/rest-api.md`'s discrepancies-endpoint row gained one additive field,
`evaluationId` per total row, needed by the frontend to address the `PUT`. Frontend:
`features/discrepancy/discrepancy-alert.component.ts` (route `/judge/tables/:tableId/
discrepancies`) lists open alerts involving the caller — blind code, a totals comparison table
marking the caller's own row — with an "Adjust my evaluation" action revealing a duplicated (not
shared, same-convention-as-the-backend's validators) copy of the five-section score/comment form;
not routed through `SyncService`'s offline outbox, since this repair flow is framed by the spec as
online-only ("shown to each involved judge as soon as they are next online"). `judge-table-order.
component.ts` gained a live open-discrepancy-count banner (re-fetched, not derived, on
`DiscrepancyRaised`/`DiscrepancyResolved` — same "events are notifications" convention as every
other hub consumer here) and a link out to the discrepancy page from its pre-existing
`discrepancy-open` close-error branch; `evaluation-sheet.component.ts`'s "already evaluated" locked
message now branches for `PendingConsensus` specifically, linking to the discrepancy page instead of
a dead end. E2E (`us11-discrepancy.spec.ts`) proves the full loop across two independent judge
sessions: 15-point-apart submission → both provisional + alert reaches both sessions (one live via
the hub within ~1s, matching this codebase's other realtime-propagation budgets) → close blocked
(`409 discrepancy-open`) → adjustment within 7 points → resolved (live on the other session too) →
close succeeds.

**Phase 14 (User Story 12 — Live Judge Removal for Conflict of Interest, T084–T088)** is now
**complete**. Backend: `Features/Tables/RemoveJudge.cs` (`DELETE /competitions/{id}/tables/{tableId}/
judges/{judgeId}`, ORGANIZER-only) scopes the target `TableJudge` row through `TastingTable.
CompetitionId` (the composite-key `TableJudge` has no `CompetitionId` of its own) so a table/judge id
from a different competition can't be addressed via a route naming a competition the caller does own,
then soft-revokes by setting `RemovedAt` — rows are never hard-deleted once evaluations exist
(data-model.md), so already-submitted evaluations stay untouched and readable. Once that `RemovedAt`
flush is committed, the handler re-reconciles every `Open` `DiscrepancyAlert` at the table (looping
`DiscrepancyReconciler.ReconcileAndSaveAsync` per affected `BeerEntryId`, inside the same transaction)
— without this, an alert the removed judge was party to could never resolve, since the only other
callers of reconciliation (`SubmitEvaluation`, `AdjustEvaluation`) both require an active table
membership the removed judge no longer has, and `CloseTable` hard-blocks on any `Open` alert, so the
table could otherwise never close again. `DiscrepancyReconciler.ReconcileAsync` itself now excludes
removed judges' totals from the involvement math entirely (fetched fresh from `TableJudges` inside the
same call, benefiting all three call sites, not just this new one) and leaves a removed judge's own
`Evaluation.Status` untouched rather than flipping it to `Confirmed` — reconciling toward consensus is
a live-table concept that no longer applies to someone who has left. A `DiscrepancyResolved` event
(same wire shape and dual-audience routing as `AdjustEvaluation.cs`'s equivalent) is published, after
commit, for each alert this removal actually resolved. Removal is gated to
competition state `InEvaluation` only (`409 invalid-state-transition` otherwise, FR-039 scopes this to
"the live event"). That gate also closes a real data-integrity hole: `TableAssignmentApplier` (behind
`UpdateTable`'s Draft/Active `PUT`) filters on `RemovedAt == null` to compute who to re-add, so a
soft-removed judge re-added via a Draft/Active `PUT` would otherwise collide with the still-tracked row
on `TableJudge`'s composite PK — since `UpdateTable` already refuses writes once the competition reaches
`InEvaluation`, gating removal to that same state makes the two paths mutually exclusive. No new
membership guard was needed anywhere: `JudgeTableAccess.FindActiveMembershipAsync`, already filtering
`RemovedAt == null` and reused by every judge-workspace slice (`SubmitEvaluation`, `FixOrder`,
`GetTableSamples`, `GetMyTables`, both `Discrepancy.cs` handlers, `CloseTable`) plus
`CompetitionHub.JoinTable`'s own independent filter, starts rejecting the removed judge everywhere the
instant this handler commits. The read-then-flip is guarded against a double-removal race the same way
`CloseTable.cs` guards its one-shot state flip (that exact class of bug was a real senior-code-reviewer
finding on `CloseTable.cs` in PR #23 — see Recorded debt below): a transaction re-fetches the row with
`FromSqlInterpolated(... FOR UPDATE)`, scoped by both halves of the composite key (`TastingTableId`,
`JudgeId`, since there's no single `Id` column to lock by), and re-checks `RemovedAt` after acquiring
the lock before mutating — stress-tested directly with 50 concurrent `DELETE`s against the same pair
(1x200, 49x404, exactly one audit row, zero exceptions). Audit/SignalR ordering matches `CloseTable.cs`
exactly: stage the audit record (entity id is a SHA-256 hash of `"{tableId}:{judgeId}"` truncated to 40
hex chars — `RemoveJudgeRules.ComputeAuditEntityId` — since `AuditLog.EntityId` is capped at 50 chars
and the composite key has no single Guid), `SaveChangesAsync`, `CommitAsync`, only then publish two
`JudgeRemoved` events (contracts/signalr-hub.md): `{ tableId, judgeId }` to the `table:{tableId}` group
so the removed judge's own client ejects, `{ tableId, judgeId, judgeDisplayName }` to the
`competition:{id}:organizers` group as a confirmation echo. Frontend: `core/api/tables-api.service.ts`
(promoted to `core/` immediately, since both the dashboard and table-management features consume it)
adds `removeJudge()`; `competition-monitor.component.ts` lists each table's judges with a Remove action
behind a confirm modal, applying the response optimistically to local state (no refetch — the `DELETE`
response is the source of truth). `core/offline/db.ts` gained a Dexie schema v2 migration indexing
`drafts` by `tastingTableId` (previously keyed only by `beerEntryId`), so a table's drafts can be looked
up directly rather than only through a matching outbox row. `core/offline/sync.service.ts` gained two
purge paths for "outbox items for this table surfaced as rejected":
`rejectOutboxForTable(tastingTableId)`, called once a judge's own session confirms a live
`JudgeRemoved` means them, deletes every outbox row for that table *and*, via the new index, every
draft for that table even when it has no matching outbox row (a sample still mid-edit, never
submitted — previously such drafts leaked forever since nothing else ever revisited them) — and records
what was lost in a new `rejectedSubmissions` signal; a lazy path inside `replayOutbox()`'s background
sweep purges the same way when a queued row 404s having missed the live event entirely (app
closed/offline at removal time). `submit()`'s foreground path treats a `404` as a definitive rejection
alongside the existing `400`/`409` set, and `evaluation-sheet.component.ts`'s `onSubmit` now calls
`handleEjected()` immediately on that `404` — navigate + purge right away — instead of only surfacing an
inert error message and relying solely on the live `JudgeRemoved` hub event to eventually catch it,
since that event may never arrive on a dropped/reconnecting connection; the submit-error mapping's old
dedicated 404 message branch was removed as redundant now that the 404 path ejects instead of rendering
text. Both `judge-table-order.component.ts` and `evaluation-sheet.component.ts` also still subscribe to
`JudgeRemoved` directly (filtered to their own `tableId` via the same generic
`CompetitionHubService.on<K>` pattern every other hub event uses) as the primary live-eject path; since
the judge-facing event payload never carries the current session's own judgeId (anonymity-adjacent
constraint — same reasoning as the blind-DTO invariant elsewhere), each re-verifies by calling its own
`GET .../samples` again — a `404` means *this* judge was the one removed (a different outcome means it
was someone else at the same table, a no-op) — then calls `rejectOutboxForTable` and navigates to
`/judge/tables` with `{ ejected: true, tableName }` in route state, identical shape in both components.
`judge-tables-list.component.ts` reads that one-shot `history.state` pair and shows a dismissible "You
were removed from {tableName} by the organizer." banner, falling back to generic wording only when
`tableName` is genuinely unavailable. E2E (`us12-removal.spec.ts`) proves the full loop with two
independent judge sessions: judge A submits one evaluation and leaves a second in progress, the
organizer removes them via the real dashboard UI, judge A's session ejects within ~1s to the named
banner and a subsequent request 404s, the organizer's dashboard drops judge A's row (judge B's stays)
and the audit drill-down still shows judge A's earlier submitted total.

## Local topology (.NET Aspire — `dotnet run --project backend/src/BirraPoint.AppHost`)

| Resource | Implementation | Local endpoint | Notes |
|---|---|---|---|
| `postgres` / database `db` | `postgres:16` container, persistent data volume, persistent lifetime | dynamic port | connection string injected into the API as `ConnectionStrings__db` |
| `keycloak` | `quay.io/keycloak/keycloak:26.2` container via `AddContainer` (ADR-0001) | http://localhost:8081 | realm `birrapoint` auto-imported from `infra/keycloak/` (roles `ORGANIZER`/`JUDGE`, seeded organizer, PKCE SPA client, admin service-account client with `manage-users`); bootstrap/realm credentials are local-dev placeholders (FR-046) |
| `mailpit` | CommunityToolkit MailPit integration | dynamic SMTP · **http://localhost:8025 (T040, pinned)** | local mail sink for invitations/results; UI/API port fixed (`AddMailPit("mailpit", httpPort: 8025)`) so `frontend/e2e/us4-judges.spec.ts` can poll its REST API deterministically — SMTP endpoint stays dynamic, only injected into the API via `Smtp__Host/Port` |
| `api` | `BirraPoint.Api` project | http://localhost:5121 · https://localhost:7075 (launchSettings) | receives env: `Keycloak__Authority` (realm URL), `Keycloak__AdminClientId/Secret` (dev placeholder), `Smtp__Host/Port` (from the Mailpit endpoint), `Frontend__BaseUrl` (T041, invitation email login link); waits for the database |
| `frontend` | `npm start` (ng serve) via `AddNpmApp` | http://localhost:4200 (non-proxied) | matches the SPA client redirect URIs; waits for the API |

## Backend (`backend/`, .NET 10 / C# 14)

- **Projects** (`BirraPoint.sln`): `BirraPoint.Api` (modular monolith; `Domain/` shared kernel +
  `Common/Persistence/` now populated, `Features/ Realtime/` still empty), `BirraPoint.AppHost`
  (Aspire SDK 13.4.6), `BirraPoint.ServiceDefaults`, tests `BirraPoint.Api.UnitTests` and
  `BirraPoint.Api.IntegrationTests`.
- **NuGet Central Package Management** (`backend/Directory.Packages.props`): all package versions
  live there (`ManagePackageVersionsCentrally`), csprojs carry version-less `PackageReference`s;
  the shared `$(BirraPointTargetFramework)` property (currently `net10.0`) defined in the same
  file is the single place to bump the target framework.
- **`BirraPoint.Api`**: `AddServiceDefaults()` + `MapDefaultEndpoints()`, plus (T009)
  `AddDbContext<AppDbContext>` wired to the `db` connection string and `Database.MigrateAsync()`
  run on startup **in Development only**. Pipeline order (T011/T012/T020):
  `UseExceptionHandler()` → `UseCors()` (Development only) → `UseAuthentication()` →
  `UseAuthorization()` → endpoint mapping. **CORS** (T020): a default policy allowing only
  `http://localhost:4200` (`AllowAnyHeader`/`AllowAnyMethod`, no `AllowCredentials` — auth is
  bearer-token via header or SignalR's `?access_token=`, never cookies), registered and applied
  in Development only; production topology (same-origin behind ACA ingress, or a real allowed
  origin) is a Phase 16 decision. Added alongside the frontend's `ApiClient`/`CompetitionHubService`
  because nothing had called the API cross-origin from a browser before T020 — verified with a
  live `fetch` from `localhost:4200` to `localhost:5121` in T020's browser check.
  **Current HTTP surface**: `/health` (all checks) and `/alive` (checks tagged `live`), both
  mapped in Development only (stock ServiceDefaults guard; ACA probes will need a scoped exposure
  decision in Phase 16) and explicitly `.AllowAnonymous()` since T011's deny-by-default fallback
  policy would otherwise block unauthenticated container/Aspire probes; `/openapi/v1.json` (T017,
  `AddOpenApi()`/`MapOpenApi()`) with Swagger UI on top at `/swagger`
  (`Swashbuckle.AspNetCore.SwaggerUI` — UI middleware only, document generation stays with the
  built-in generator; Development only, like the document); and the first business endpoint,
  `GET /api/v1/styles` (T017, any authenticated caller per the fallback policy).
- **`Common/Auth/`** (T011, audience validation closed T017/ADR-0009): `AddKeycloakAuthentication`
  wires JWT bearer (`Authority` from `Keycloak:Authority` config, `MapInboundClaims = false` to
  keep Keycloak's raw claim names, `RequireHttpsMetadata` off only in Development,
  `ValidateAudience = true` with `ValidAudience` from `Keycloak:ApiAudience` — the realm's
  `birrapoint-spa` client now carries an `oidc-audience-mapper` protocol mapper stamping
  `birrapoint-api` onto every access token) plus a deny-by-default fallback authorization
  policy and `ORGANIZER`/`JUDGE` role policies. `KeycloakRolesClaimsTransformation`
  (`IClaimsTransformation`) maps Keycloak's nested `realm_access.roles` claim into individual
  `ClaimTypes.Role` claims so `[Authorize(Roles=...)]`/`IsInRole` work; it is idempotent since
  ASP.NET Core may invoke a claims transformation more than once per request.
  `ICurrentUser`/`CurrentUser` expose `Sub`/`Email`/`Roles` for the authenticated caller via
  `IHttpContextAccessor`.
  **Organizer table (2026-07-29)**: new `Domain/Organizer.cs` (`KeycloakUserId`/`Email` unique,
  `FirstName`/`LastName`) models one-organizer-to-many-competitions explicitly, resolved-or-created
  lazily via `IOrganizerResolver`/`OrganizerResolver` (`Common/Auth/OrganizerResolver.cs`, mirrors
  `JudgeResolver`'s shape but *creates* rather than only backfills, since organizers self-register —
  no row can pre-exist before their first authenticated write). `ICurrentUser`/`CurrentUser` gained
  `GivenName`/`FamilyName` (Keycloak's `given_name`/`family_name` claims, read pattern identical to
  `Email`) and `GetOrganizerAsync(ct)`. `Competition` gained a new **nullable** `OrganizerId` FK
  (`OnDelete(DeleteBehavior.Restrict)`), populated going forward by `CreateCompetitionCommandHandler`
  alongside — not instead of — the existing `CreatedByUserId` claim string, which stays the source
  of truth for every one of the ~25 existing `c.CreatedByUserId == currentUser.Sub` ownership checks
  across the codebase; none of those were touched. Deliberately scoped this way (additive FK, no
  migration of existing call sites) to avoid a large, hard-to-verify blast radius. The
  `AddOrganizers` EF Core migration (`Migrations/20260729154727_AddOrganizers.cs`) has since been
  generated and verified: `Organizers` table with unique indexes on `KeycloakUserId` and `Email`,
  nullable `OrganizerId` + FK/index on `Competitions` (`ON DELETE RESTRICT`); full unit suite and
  `OrganizerResolverTests` pass against a real Postgres via Testcontainers. Since T015,
  `AddKeycloakAuthentication` also wires
  `JwtBearerEvents.OnMessageReceived` to read the token from `?access_token=` on the
  `/hubs/competition` path only (browser WebSocket handshakes can't set an `Authorization` header)
  — every other endpoint is unaffected and still requires the header (ADR-0006).
  **T023** adds `Name` (Keycloak's `name` claim, same read pattern as `Email`) and
  `GetJudgeRecordsAsync(ct)` to `ICurrentUser`/`CurrentUser`, delegating to the new
  `IJudgeResolver`/`JudgeResolver` (`Common/Auth/JudgeResolver.cs`, `AddScoped`):
  `ResolveAndBackfillAsync(sub, email, name, ct)` matches **every** `Judge` row across
  competitions sharing `email` — the COI key is `(CompetitionId, Email)`, not globally unique, so
  one person can judge several competitions with separate rows — and backfills `KeycloakUserId`
  once per row (idempotent: an already-backfilled row is left untouched on replay) plus
  `DisplayName` only when a non-null `name` is supplied. Takes primitives rather than
  `ICurrentUser` itself to avoid a DI cycle (`CurrentUser` calls *into* this). **T052**:
  `GetJudgeRecordsAsync` now has its first real caller — `Features/TastingOrder/`'s
  `JudgeTableAccess` helper resolves the caller's Judge row ids through it before checking active
  `TableJudge` membership. `CompetitionHub.JoinTable`'s own inline email/`KeycloakUserId` fallback
  (below) is still intentionally left as-is, not refactored to call this resolver, since it reads
  `Context.User` not `ICurrentUser` (ADR-0006) and no task has asked for a hub change.
- **`Common/Errors/`** (T012): ProblemDetails via the .NET `IExceptionHandler` chain (tried in
  registration order): `DomainExceptionHandler` (maps `DomainException` to its catalogued urn),
  `ValidationExceptionHandler` (maps FluentValidation's `ValidationException` to `400` +
  a per-field error map), `FallbackExceptionHandler` (logs server-side, returns a generic `500`
  that never includes the exception message or type — Principle VII). `DomainErrorType` is a
  compiler-checked enum for the 14 closed-catalog entries from contracts/rest-api.md §Error
  catalog; `DomainErrorCatalog` holds their urn/status/title. No slice throws `DomainException`
  yet — T017's `GetStyles` has no error path (contract defines only `200`).
- **`Common/Behaviors/`** (T013): `AddMediatRWithValidation` registers MediatR handlers,
  auto-discovers FluentValidation validators (`AddValidatorsFromAssembly` —
  `FluentValidation.DependencyInjectionExtensions` package, separate from core `FluentValidation`),
  and adds `ValidationBehavior<,>` as an open pipeline behavior. `ValidationBehavior` runs every
  `IValidator<TRequest>` for the request and throws FluentValidation's `ValidationException` on
  any failure (no-op if none are registered) — T012's `ValidationExceptionHandler` maps that
  straight to a `400 urn:birrapoint:validation`. No slice/validator exists yet (first is T025+).
- **`Common/Audit/`** (T014): `IAuditWriter`/`AuditWriter.Record(action, entityType, entityId,
  before?, after?)` — synchronous, no I/O: reads the actor from `ICurrentUser.Sub` (T011) and
  stages an `AuditLog` row via `AppDbContext.AuditLogs.Add(...)`. Deliberately does **not** call
  `SaveChangesAsync` itself, so the audit entry commits atomically together with whatever
  business change the caller's own handler persists, in the same transaction. `DataJson` is
  `{ "before": ..., "after": ... }`.
- **`Domain/`** (T008, expanded T010): 14 entities and 7 enums per `data-model.md` —
  `Competition`, `BjcpStyle`, `Participant`, `BeerEntry`, `EntryCollaborator`, `Judge`,
  `Invitation`, `TastingTable`, `TableJudge`, `TableSample`, `Evaluation`, `DiscrepancyAlert`,
  `DispatchJob`, `AuditLog`; `CompetitionState`, `TableState`, `EvaluationStatus`,
  `InvitationStatus`, `DiscrepancyStatus`, `DispatchJobType`, `DispatchJobStatus`. POCOs only —
  no business logic; `Entity`/`ITimestamped` provide the Guid v7 PK and `CreatedAt`/`UpdatedAt`
  contract. `BjcpStyle` (T010, FR-049) carries vital statistics (`OGLow/OGHigh`, `FGLow/FGHigh`,
  `IBULow/IBUHigh`, `SRMLow/SRMHigh`, `ABVLow/ABVHigh`, all nullable) plus `DescriptionJson`
  (jsonb: overall impression, aroma, appearance, flavor, mouthfeel, comments, history,
  characteristic ingredients, style comparison, entry instructions, commercial examples, tags) —
  not just the original code/name/category import-matching fields.
- **`Common/Persistence/`** (T009, expanded T010): `AppDbContext` (one
  `IEntityTypeConfiguration<T>` per entity under `Configurations/`), stamping
  `CreatedAt`/`UpdatedAt` centrally in `SaveChanges(Async)`; `DesignTimeDbContextFactory` for
  `dotnet ef` tooling; `Migrations/` holds `InitialCreate` and `AddBjcpStyleCatalogDetails`.
  Encodes every constraint named in `data-model.md`: unique `(JudgeId, BeerEntryId)` (idempotency
  backstop, FR-029), `Evaluation.Total` as a **stored computed column** (never client-writable,
  FR-024), `EndDate >= StartDate` + registration-window check constraints on `Competition`,
  partial unique index on `DiscrepancyAlert` (`WHERE "Status" = 'Open'`), unique `(BeerEntryId)`
  on `TableSample` (an entry sits at one table), unique `(TastingTableId, SequenceOrder)`.
  State/status enums are stored as strings (ADR-0004), not the EF default int.
  `BjcpStyle.Code`/`BeerEntry.StyleCode` are `varchar(20)` (widened from the originally-planned 5
  — synthetic slug codes for styles without an official BJCP letter subcode run up to 17 chars).
- **`Features/Catalog/Data/`** (T010): `bjcp-2021.json` only — the full BJCP 2021 catalog, 125
  entries (categories 1–34 + Appendix B local styles X1–X5), marked `EmbeddedResource` in the
  csproj so it ships inside the compiled assembly (available identically in dev, CI/Testcontainers,
  and containers, regardless of working directory). Pure data, no code, in this folder.
- **`Common/Persistence/Seeding/`** (T010): `BjcpStyleCatalogLoader` reads the embedded JSON via
  `Assembly.GetManifestResourceStream`; `BjcpStyleSeedRecord`/`VitalStatisticsSeed`/
  `StyleDescriptionSeed` are the deserialization DTOs; also exposes `ComputeContentHash()`
  (SHA-256 of the raw resource bytes), pinned by a unit test so an in-place edit to the JSON after
  the seed migration ships fails fast instead of silently diverging across environments. This
  lives in `Common/Persistence/`, not `Features/Catalog/`, because the seed migration (shared
  kernel) must never depend on a feature slice. The `AddBjcpStyleCatalogDetails` migration's
  `Up()` calls the loader and seeds all 125 rows via `migrationBuilder.InsertData` (ADR-0005) —
  the JSON file is the only place the catalog content itself lives; the migration never
  hardcodes it.
- **`Features/Catalog/`** (T017, first REST slice): `GetStyles.cs` holds the whole vertical slice
  in one file per the backend convention — `GetStylesQuery` (no parameters, so no
  FluentValidation validator), `GetStylesQueryHandler` (projects `AppDbContext.BjcpStyles` to
  `StyleSummaryDto`, then sorts client-side by category number then `Code`; `CategoryNumber` is a
  varchar — Appendix B local styles use `"X"` — so a plain SQL `OrderBy` would sort
  lexicographically, `"1", "10".."19", "2", "20"...`, instead of by actual category order, per
  senior-code-reviewer T017 review), and `MapCatalogEndpoints` mapping `GET /api/v1/styles`. No
  explicit role policy — "any authenticated" per contracts/rest-api.md is already satisfied by
  the deny-by-default fallback policy. `GetStyleDetail` (`GET /styles/{code}`) lands later with
  T059B/FR-049.
- **`Features/Competitions/`** (T025–T028, US2): the competition creation wizard + lifecycle, all
  `ORGANIZER`-only. `CompetitionStateMachine.CanTransition(from, to)` is the single pure/static
  FR-006 gate (forward-only, skip-free — a lookup table of the one legal next state per state, so
  reverse/same-state/skip-ahead all fall through to `false`); both `UpdateCompetitionCommandHandler`
  (edits only while `Draft`/`Active`) and `ChangeCompetitionStateCommandHandler` (any target) read
  off the same enum, only the latter needs the transition-gate helper itself.
  `CreateCompetition.cs`/`UpdateCompetition.cs` carry matching `AbstractValidator`s (required
  `Name`/`Venue`, `EndDate >= StartDate`, `EntryLimit > 0` when set, `RegistrationEnd >=
  RegistrationStart` when both set — the T009 DB check constraints stay a last-resort backstop, not
  the primary validation path). `GetCompetition.cs`/`ListCompetitions.cs`/`UpdateCompetition.cs`/
  `ChangeState.cs` all scope by `CreatedByUserId == currentUser.Sub` and return `null` on a miss —
  `CompetitionsEndpoints` maps that to a plain `404` (never a `DomainException`/urn, since scope
  misses aren't in the 14-entry catalog and must never reveal cross-owner existence — this is the
  exact pattern T021's `/__test/` diagnostic endpoints stood in for before this slice existed).
  `ChangeCompetitionStateCommandHandler` additionally gates `Finalized` on every `TastingTable`
  being `Closed` (`409 tables-still-open` with the open ids in the ProblemDetails extensions —
  vacuously satisfied today since no competition has any tables yet), stages an audit entry via
  `IAuditWriter` before its own `SaveChangesAsync`, then is the **first real story-driven emitter**
  on `CompetitionHub`: `CompetitionStateChanged` (`{ competitionId, state }`) to the
  `competition:{id}:organizers` group, fired only after the transaction commits.
  `CompetitionsEndpoints.MapCompetitionsEndpoints` maps all five endpoints under one
  `RequireAuthorization("ORGANIZER")` route group; `POST`/`PUT`/`POST .../state` bind their MediatR
  command straight from the JSON body (record constructor binding, no extra DTO) and combine it
  with the route's `{id}` via `command with { Id = id }` before sending. `Competition.State`
  defaults to `Draft` at the entity level (`Domain/Competition.cs`), so `CreateCompetitionCommandHandler`
  never sets it explicitly — every created competition already satisfies FR-008 ("save as Draft at
  any step") the moment it exists, with no separate draft-persistence mechanism needed.
  **`GetCompetitionCategories.cs`/`SetCompetitionCategories.cs`** (T104, FR-052): a brand-new,
  organizer-defined grouping layer bolted onto this same slice — `CompetitionCategory` (free-text
  `Name` chosen by the organizer, e.g. "Estilos clásicos") and the join `CompetitionCategoryStyle`,
  both under `Domain/`, distinct from `BjcpStyle.CategoryName`/`CategoryNumber` (the BJCP
  taxonomy's own official category — XML doc comments on both entities call this out explicitly to
  head off confusion). `CompetitionCategoryStyle` carries a denormalized `CompetitionId` alongside
  its real FK path through `CompetitionCategoryId`, purely so `IX_CompetitionCategoryStyles_
  CompetitionId_StyleCode` (unique) can enforce "a style belongs to at most one category per
  competition" at the DB level without a cross-table subquery constraint; that second FK is
  `DeleteBehavior.NoAction` (not `Cascade`) since the `CompetitionCategory→Competition` cascade
  path already handles cleanup and Postgres/EF reject two independent cascade paths converging on
  the same ancestor from one descendant table. `SetCompetitionCategoriesCommand` is a full-replace
  `PUT` (same convention as `UpdateCompetition`) — deletes every existing category for the
  competition and re-inserts the submitted set inside one `SaveChangesAsync`; its validator layers
  `Cascade(CascadeMode.Stop)` + `DependentRules` (mirroring `ResolveRowCommandValidator`'s own
  DB-backed `MustAsync` pattern) so the async "do these style codes exist in `BjcpStyles`" check
  never runs once a cheaper sync rule (non-empty categories, ≥1 style assigned overall, unique
  category names, no style repeated across categories in the payload) has already failed. Both
  handlers share ownership/state-gate semantics with `UpdateCompetition` (`404` on a scope miss,
  `409 invalid-state-transition` outside `Draft`/`Active`) and reuse the existing 14-entry error
  catalog — no new `DomainErrorType` was needed, every failure here is a plain FluentValidation
  `400`. **T106 (FR-053)** later wired this allow-list into `Features/Import`'s own validation —
  see below.
- **`Features/Import/`** (T031–T035, US3): the `.xlsx` bulk-entry import + in-flow correction
  slice, `ORGANIZER`-only, under `/api/v1/competitions/{id}/imports`. `ImportBatch`/`ImportRow`
  (T033) are slice-owned staging entities — deliberately not in `Domain/`, since they exist only
  to hold parsed-but-not-yet-consolidated rows, not the domain model itself — with their own EF
  configs/migration (`AddImportBatchAndImportRow`) and are documented in `data-model.md` per
  Principle X. `WorkbookParser` (T034, ClosedXML) implements `contracts/import-file.md` exactly:
  first worksheet only, header row matched case-insensitively/trimmed regardless of column order,
  stops at the first fully empty row, per-row status `Valid`/`StyleMismatch`/`Invalid` (missing
  required cell, bad email, or the second occurrence of a duplicate `(ParticipantEmail, BeerName)`
  pair), style matched against `BjcpStyles` by exact code or exact name (case-insensitive
  comparison, no fuzzy matching, per FR-010). `UploadImport` discards any prior unconsolidated
  batch for the competition first (single active batch per competition, per the contract) and
  rejects with `409 invalid-state-transition` outside `Draft`/`Active` — reusing
  `DomainErrorType.InvalidStateTransition` by analogy with `ChangeState.cs`'s FR-006 gates rather
  than adding a new catalog entry, since the 14-urn list is closed (Principle VI). `ResolveRow`
  handles `assign-style` (FluentValidation `MustAsync` checks the style code exists in the
  catalog, `400` otherwise — kept in the validation pipeline rather than a handler-thrown
  `DomainException`, matching the repo convention that `urn:birrapoint:validation` is exclusively
  FluentValidation-produced) and `exclude`; both set the row to a fourth `ImportRowStatus.Excluded`
  value beyond the contract's three parse-time statuses, needed so an excluded row is
  distinguishable from `Invalid`/`StyleMismatch` for the unresolved-row gate and countable
  separately on consolidation. **`assign-style` is restricted to `StyleMismatch` rows** (`400
  invalid-import-file` otherwise) — an `Invalid` row is broken for a reason a style code can't fix
  (missing/malformed required cell), and the original implementation let it through anyway; caught
  by senior-code-reviewer's PR pass (would have produced an unhandled `500`/NOT-NULL violation at
  consolidation for a null `ParticipantEmail`/`Name`/`BeerName`), fixed before merge, now covered
  by both an integration test and a frontend test asserting the style picker only renders for
  `StyleMismatch` rows. `ConsolidateImport` blocks with
  `409 unresolved-import-rows` (row numbers in the ProblemDetails extensions) while any row is
  still `StyleMismatch`/`Invalid`, and **rejects a batch that's already `Consolidated`** with
  `409 invalid-state-transition` (same review pass — re-POSTing `/consolidate` on a finished batch
  previously re-ran the whole creation loop with no idempotency guard, producing duplicate
  `BeerEntry` rows); on success it dedupes `Participant`s by email within the
  competition (loaded into an in-memory dictionary up front, since a per-row query wouldn't see
  participants created earlier in the same loop before `SaveChanges`), creates `BeerEntry`/
  `EntryCollaborator` rows, and generates a unique-per-competition `BlindCode` via
  `BlindCodeGenerator` (collision-checked against existing codes). `ImportEndpoints` mirrors
  `CompetitionsEndpoints`'s route-group/`RequireAuthorization("ORGANIZER")` shape; the multipart
  upload endpoint binds `IFormFile` directly and carries `.DisableAntiforgery()` (no cookie-based
  auth on this API, Principle VII, so CSRF protection is moot for a bearer-token endpoint).
  **T106-T108 (FR-053/FR-054)**: closed the deferral above. A fifth `ImportRowStatus.
  CategoryStyleMismatch` covers a row whose category and style each resolve individually but
  aren't an allowed pair under that competition's `CompetitionCategoryStyle` allow-list — checked
  at parse time (`WorkbookParser`, loading the pairs alongside styles/categories in
  `UploadImport`), at the full-row edit (`EditImportRow`'s completeness check), and at consolidation
  (`ConsolidateImport`'s unresolved-row gate now includes it). New `POST .../imports/{importId}/
  revalidate` (`RevalidateImport.cs`) re-resolves every non-`Invalid`/`Excluded` row against the
  competition's *current* categories/allow-list without re-uploading the file — needed because
  `SetCompetitionCategories`'s full-replace `PUT` deletes and recreates `CompetitionCategory` rows,
  so a staged row's already-resolved `CompetitionCategoryId` can go stale even when a same-named
  category still exists; it re-matches by the row's original raw cell text in that case instead of
  trusting the stale id, and deliberately does *not* clear an already-resolved style just because
  its category needs re-resolving (preserves the organizer's prior correction work). No-op once the
  batch is `Consolidated`.
- **`Common/Keycloak/`** (T040, R-10): `IKeycloakAdminClient`/`KeycloakAdminClient` — kept to the
  two calls this codebase actually needs, not a general Admin SDK wrapper. Client-credentials
  grant against `{Keycloak:Authority}/protocol/openid-connect/token` using
  `Keycloak:AdminClientId`/`Keycloak:AdminClientSecret` (already wired since T009/`AppHost.cs`;
  realm.json's `birrapoint-api-admin` service-account client already carries `manage-users`/
  `view-users`); the admin REST base is derived from `Authority` by swapping `/realms/` for
  `/admin/realms/` in the same URL — no separate config key needed.
  `EnsureUserWithTemporaryPasswordAsync(email)` is idempotent on an existing account (one
  person's Keycloak user can be shared across competitions, per `JudgeResolver`'s own comment):
  finds-or-creates the user, ensures the `UPDATE_PASSWORD` required action, resets the password
  with `temporary: true`, and returns the plaintext password — **never persisted anywhere**, the
  caller emails it once and discards it. `UpdateUserEmailAsync` is a no-op if no Keycloak account
  exists yet for the old address (the judge's invitation hasn't been dispatched yet).
- **`Common/Email/`** (T041, R-10): `IEmailSender`/`MailKitEmailSender` — one method,
  `SendAsync(toEmail, subject, htmlBody)`, against `Smtp:Host`/`Smtp:Port` (already wired to
  Mailpit locally, `SecureSocketOptions.None`, no auth needed for Mailpit).
- **`Features/Judges/`** (T038, T042, US4): bulk judge registration + invitation dispatch,
  `ORGANIZER`-only, under `/api/v1/competitions/{id}/judges`. Dedup/classification is a pure
  static helper, `JudgeRegistrationPlanner.Plan(emails, existingEmails)` — fully unit-testable
  without a DB round-trip (T038): case-insensitive within-list duplicates → skipped
  `duplicate-in-list`, emails already registered for the competition → skipped
  `already-registered`, everything else → created. `RegisterJudgesCommandHandler` does only DB
  writes (creates `Judge` + `Invitation(Pending)` rows) and, **after its own `SaveChangesAsync`**
  (same after-commit convention as every other `IDispatchJobQueue`/`IEventPublisher` call in this
  codebase), enqueues one `DispatchJobType.SendInvitation` job per newly created judge — fast bulk
  response, no N synchronous Keycloak/SMTP calls blocking the HTTP request.
  **`SendInvitationHandler`** (T041, `IDispatchJobHandler`, registered
  `services.AddScoped<IDispatchJobHandler, SendInvitationHandler>()` — auto-discovered by
  `DispatchWorker`, T016, which is this handler's first real consumer) is where Keycloak
  provisioning actually happens: it calls `IKeycloakAdminClient.EnsureUserWithTemporaryPasswordAsync`
  fresh on every delivery attempt (never stored on the `DispatchJob` payload — the payload is just
  `{ JudgeId }`), builds the invitation email, sends it, and updates `Invitation.Status`/`SentAt`
  on success or `Attempts`/`LastError`/`Status = Failed` on failure before rethrowing so
  `DispatchWorker`'s existing `DispatchRetryPolicy` backoff handles the reattempt — no bespoke
  retry loop. **`Judge.KeycloakUserId` is never written by this slice** — it stays `null` at
  creation and is only ever backfilled by the existing `JudgeResolver` (T023) the first time the
  judge's account actually authenticates; `judge-already-active` (`UpdateJudgeEmail`'s `409` gate)
  keys directly off that field being non-null. `ResendInvitation` resets `Invitation.Status` to
  `Pending` (leaves `Attempts` as a running total, mirroring `DispatchJob.Attempts`'s own
  convention) and re-enqueues the same job type. `UpdateJudgeEmail` re-validates uniqueness and
  updates the Keycloak account email; see the scope note above and Recorded debt below for what it
  deliberately does not yet do.
- **`Features/Tables/`** (T045, T047, US5): table setup with transactional COI validation and
  competition-wide BOS flag/unflag, `ORGANIZER`-only under `/api/v1/competitions/{id}/tables` —
  **no migration needed**, `TastingTable`/`TableJudge`/`TableSample` were fully scaffolded since
  Phase 2 with every constraint already DB-enforced (unique `(CompetitionId, Name)`, unique
  `BeerEntryId` on `TableSample`). Pure logic is split out for unit-testability without Postgres:
  `CoiDetector.FindConflicts(judgeEmails, beerEntryIds, entryOwnerEmails)` (FR-017 — owner-or-
  collaborator email match, grouped per judge) and `BosFlagRules` (FR-018 —
  `IsEligibleForUnflag(remainingAssignments, hasEvaluated)` encodes the permanence rule: `false`
  once `hasEvaluated` is true regardless of remaining assignments, even though
  `Features/Evaluations` doesn't exist until Phase 9 — genuinely testable today by seeding an
  `Evaluation` row directly via `AppDbContext`, not dead code). `CreateTable`/`UpdateTable` both
  take the **full desired state** (`{ name, judgeIds, beerEntryIds }`, replace semantics, not
  incremental) and share one core, `TableAssignmentApplier.ApplyAsync`: validates COI over the
  complete submitted set **before any mutation** (`409 conflict-of-interest`, `{conflicts:
  [{judgeId, beerEntryIds}]}` — nothing persisted, same check-before-`SaveChangesAsync` atomicity
  pattern as `Features/Import/ConsolidateImport.cs`), diffs current vs. submitted `TableJudge`/
  `TableSample` rows (hard-deletes what's no longer present — safe pre-Phase-9, no evaluations can
  exist yet to protect), then flags every newly-assigned judge's owner-or-collaborator entries
  competition-wide, and unflags a removed judge's entries only once **every** owner/collaborator
  judge of that entry is clear — zero active table assignments elsewhere in the competition and
  zero `Evaluation` rows, checked per co-owner via `InvertOwnedEntriesByEmail`, not just the one
  judge who happened to leave this table. **Fixed same-day by senior-code-reviewer's PR #19
  pass**: the original version checked only the leaving judge's own remaining assignments, so an
  entry co-owned by a judge leaving Table 1 and a judge still seated at Table 2 was incorrectly
  unflagged — a real FR-018 integrity bug, not a scoping choice; caught before merge, covered by
  a new regression test. `TableValidationRules` (FluentValidation `MustAsync`, closed
  error catalog — no new `DomainErrorType` needed, `ConflictOfInterest`/`TableClosed` already
  existed) checks table-name uniqueness, that submitted judge/entry ids actually belong to the
  caller's competition (ownership-scoped the same way `UpdateJudgeEmail`'s validator was fixed to
  be — a PR #18 review finding — otherwise a submitted foreign-competition entry id could leak
  cross-tenant existence or let one organizer "steal" another's unassigned entry via the global
  `TableSample.BeerEntryId` uniqueness constraint), and that no submitted entry is already assigned
  to a *different* table. `TableProjector` builds the shared GET/response DTO — judges, samples
  (blind code, style name, ABV range, BOS flag), progress (submitted-evaluation count, vacuously 0
  today), and per-table stats (mean ABV from `BjcpStyle.ABVLow/High`, style count/list) — all
  computed server-side so the frontend never needs a second round-trip to the catalog.
  **Addendum, added mid-implementation**: `ListEntries.cs` (`GET /entries`) — a real gap found
  while scoping the frontend work, not present in any contract: nothing let the organizer list a
  competition's `BeerEntry` rows at all outside `ConsolidateImport`'s one-time response, so T048's
  "Unassigned" beer column had no data source. Returns every entry with its style/ABV and current
  table assignment (`tastingTableId`/`tastingTableName`, both `null` when unassigned).
- **`Features/TastingOrder/`** (T050–T052, US6): the first judge-facing slice, `JUDGE`-only under
  `/api/v1/me/tables` — the mirror image of `Features/Tables/`'s organizer-only shape. `JudgeDtos.cs`
  is a dedicated anonymity-boundary namespace (data-model.md §Anonymity boundary): `JudgeSampleDto`
  and `JudgeTableSummaryDto` structurally carry no entrant-identifying field, and a contract test
  asserts the serialized wire payload directly, not just the DTO's declared members. The one
  deliberate exception (ADR-0011 point 6, formalized in spec.md FR-019 as of Session 2026-08-02) is
  `EntryInstructions`: entrant-authored serving/tasting guidance some BJCP styles need, added later
  by the ACCE-import fold. The organizer *can* review/edit/clear it per row during import/
  consolidation (`import-step.component.ts`'s row editor) — that's an available capability, not a
  gate consolidation is blocked on, so its absence from a row's summary view is a known, tracked
  gap (Recorded debt below), not an oversight. `BeerName` and every `Participant.*` field remain
  permanently excluded. `JudgeTableAccess`
  (shared, all three handlers use it) resolves active table membership off `ICurrentUser.
  GetJudgeRecordsAsync()`'s backfilled Judge rows rather than re-deriving the sub/email match
  `CompetitionHub.JoinTable` does inline. `GetTableSamples` derives `evaluationStatus` (`NotStarted`
  / `Submitted` / `PendingConsensus`) by left-joining `Evaluation` scoped to the caller's own Judge
  id — genuinely wired from the start (same shape as Phase 7's `BosFlagRules`), and now that
  `Features/Evaluations` exists (Phase 9, below) samples correctly flip to `Submitted`/
  `PendingConsensus` as a judge submits. `TastingOrderRules` (pure, unit-tested without Postgres) encodes the one-shot
  check and the `Active`/`InEvaluation` state gate; `FixOrder`'s handler wraps the actual mutation
  in an explicit transaction with `SELECT ... FOR UPDATE` (`FromSqlInterpolated`) on the
  `TastingTable` row so two judges racing to fix the same table's order get exactly one `200` and
  one `409 order-already-fixed` — this is the first row-locking pattern in the codebase (every
  other one-shot/uniqueness guard so far has relied on a DB unique constraint catching a
  `DbUpdateException`, which doesn't fit here since there's no unique index to violate — "already
  fixed" is a business-state check, not a row collision). On success, emits `TableOrderFixed` (after
  commit, `CancellationToken.None`, matching every other emitter's convention) — **only to the
  `table:{tableId}` group**; contracts/signalr-hub.md also lists this event under the organizer
  group, not yet wired (Phase 11/US9's monitoring dashboard is the natural owner — see Recorded debt
  below).
- **`Features/Evaluations/`** (T055–T058, US7): the first slice that mutates domain state from a
  judge-facing endpoint, `POST /me/tables/{tableId}/evaluations`. `SubmitEvaluationRules` (pure,
  unit-tested without Postgres, same split as `TastingOrderRules`) encodes `IsNextInSequence`
  (FR-022 — the requested entry must be the first one in the fixed order this judge hasn't
  submitted yet) and `CanSubmitInState` (`InEvaluation` only). **The very first thing the handler
  does, before any precondition gate, is check for an already-persisted `(judge, entry)` row and
  return it immediately if found** (fixed same-day per senior-code-reviewer on PR #22 — see
  Recorded debt — idempotent replay must hold no matter what happened to the table/competition
  since the original successful submit); only then does it gate in order — competition state,
  order-fixed, table-open, sequence — each throwing the matching pre-existing `DomainErrorType` (no
  new catalog entries needed). **Idempotency (FR-029/R-07) is also a genuine
  insert-time race guard, the first of its kind in this codebase**: every prior unique constraint
  (blind code, participant email, the tasting-order one-shot) was pre-checked with a query before
  the write; here that's structurally forbidden — "never UPSERT" (locked-on-submit) means the check
  and the insert must be one atomic operation, so the handler just inserts and catches the Postgres
  unique-violation on `(JudgeId, BeerEntryId)` (`DbUpdateException` wrapping a `PostgresException`
  with `SqlState == PostgresErrorCodes.UniqueViolation`), re-queries whatever actually committed,
  and returns that — never assuming the retried request's body matches, since a genuine
  concurrent-race loser's body might legitimately differ from the winner's. A dedicated integration
  test fires two simultaneous submits for the same (judge, entry) via `Task.WhenAll` and asserts
  exactly one row exists — the same "prove it under a real race, not just sequential replay"
  standard `FixOrder`'s test set. `Status` was unconditionally `Confirmed` at this point in the
  build — discrepancy detection (>7-point spread → `PendingConsensus` + `DiscrepancyAlert`) didn't
  activate until Phase 13/US11 (see below), left as a one-line comment rather than a premature
  pluggable-hook abstraction at the time. Emits
  `EvaluationCompleted` (organizer group, after commit) with a freshly-computed `tableProgress`
  (completed/expected/percent across the whole table, not just this judge).
- **`Features/Catalog/GetStyleDetail.cs`** (T059B, US7, FR-049): `GET /styles/{code}`, any
  authenticated caller (same as the existing `GET /styles` list in the same file's
  `MapCatalogEndpoints` — extended, not duplicated). Projects `BjcpStyle.DescriptionJson` (parsed
  with `PropertyNameCaseInsensitive`, mirroring `BjcpStyleCatalogLoader`'s own convention) plus the
  vital-statistics columns into the judge-facing reference-panel shape; `404` for an unknown code.
  No entrant/anonymity concern — BJCP catalog data is public reference data, not competition-scoped.
- **`Features/Evaluations/CloseTable.cs` + `CorrectEvaluation.cs`** (T062–T065, US8): `CloseTable`
  (`POST /me/tables/{tableId}/close`, JUDGE, any active member) is the second slice in this folder,
  sharing a new `CloseTableRules` pure helper (missing-blind-code completeness computation, mean
  averaging — same split as `SubmitEvaluationRules`) with `CorrectEvaluation`
  (`PUT /competitions/{id}/evaluations/{evaluationId}`, ORGANIZER-only). Gates in order:
  already-closed (`409 table-closed`, reusing the existing urn rather than inventing a
  double-close-specific one), completeness (every active `TableJudge` × `TableSample` must have a
  submitted `Evaluation`, else `409 evaluations-incomplete { missing: [blindCode] }`), open
  `DiscrepancyAlert`s (`409 discrepancy-open { blindCodes }` — vacuously empty at this point in the
  build, Phase 13/US11 not landed yet). On success, one handler emits **two different `TableClosed`
  payloads** to two different
  SignalR groups — `{ tableId }` to judges, `{ tableId, consolidatedScores }` to organizers,
  matching contracts/signalr-hub.md's per-audience rows exactly rather than sending one shape to
  both. `CorrectEvaluation` is explicitly ungated by table state (the contract's whole point) —
  re-validates the same score caps/comment floor as `SubmitEvaluation` (duplicated rather than
  shared, since the two commands have unrelated shapes — sourced from the same
  `SubmitEvaluationRules` constants so the boundaries can't drift apart), lets the DB-computed
  `Total` column recompute itself, and audits via the pre-existing `IAuditWriter` convention
  (before/after snapshot, staged before the same `SaveChangesAsync` that persists the correction —
  same ordering as `ChangeState.cs`). FR-034 (immutability) needed no new guard: `SubmitEvaluation`'s
  existing `table.State != Open` check (Phase 9) already rejects post-close mutations including late
  offline syncs — `CloseTable`'s only job is to be what actually flips that flag.
- **`Realtime/`** (T015): `CompetitionHub` (`/hubs/competition`, `[Authorize]`) — server → client
  only, per contracts/signalr-hub.md. `JoinCompetitionAsOrganizer` guards on `ORGANIZER` role +
  `Competition.CreatedByUserId` ownership; `JoinTable` guards on an active (`RemovedAt == null`)
  `TableJudge` row, matched via `Judge.KeycloakUserId` or, as a bootstrap fallback before T023's
  resolver has run, `Judge.Email`; both throw `HubException` on failure (hub-only error channel,
  not the REST `urn:birrapoint:*` catalog). Reads identity from `Context.User`
  (`HubCallerContext`), not `ICurrentUser` — see ADR-0006 for why. `CompetitionGroups` holds the
  two fixed group-name formats (`competition:{id}:organizers`, `table:{tableId}`) shared by the
  hub and by `IEventPublisher`/`EventPublisher`, the generic emit-after-commit dispatcher every
  later story's handler will call after its own `SaveChangesAsync` succeeds.
  `CompetitionEvents` holds the 7 catalogued event-name constants; `DispatchWorker` (T016) would
  emit `DispatchProgress`, though no job is ever enqueued yet in practice — no slice calls
  `IDispatchJobQueue.EnqueueAsync` until T041/T075. `ChangeCompetitionState` (T028, US2, above) is
  now the first real story-driven emitter: `CompetitionStateChanged` on every FR-006 transition.
  **Known gap**: the DB-backed authorization checks
  above still have no integration/contract test — the `WebApplicationFactory` harness they need
  now exists (T018), but no task has written the hub-specific coverage yet; tracked with a
  comment in `CompetitionHub.cs`. EF Core's InMemory provider remains an unaccepted substitute
  regardless (Testcontainers-only per R-13).
- **`Common/Jobs/`** (T016, R-06): `DispatchJobQueue.EnqueueAsync` inserts a `Pending`
  `DispatchJob` row and wakes `DispatchWorker` via a shared singleton `Channel<Guid>` — no separate
  signal abstraction, the BCL channel is the wake-up mechanism directly. `DispatchWorker` (hosted
  `BackgroundService`) resume-sweeps on startup: any job still `Running` means the process crashed
  mid-handler, so it's counted as a failed attempt (not a free reset) and run through the same
  `DispatchRetryPolicy` as any other failure — capped exponential backoff (1s/2s/4s/.../60s cap),
  `MaxAttempts = 5` (not specified by the spec; an engineering choice, documented inline), `Failed`
  after that stays retryable via the API (FR-041, not built yet). The backoff is enforced, not just
  computed: a failed job's `NextAttemptAt` (ADR-0008) is set on retry, and the dispatch sweep only
  picks up `Pending` jobs whose `NextAttemptAt` has passed — otherwise any wake source (a new
  enqueue, the 30s safety-net poll, another job's own retry signal) would re-run it immediately.
  Every worker cycle runs inside a resilience boundary (`RunGuardedAsync`): a transient DB/publish
  fault is logged and backed off rather than escaping `ExecuteAsync`, which would otherwise stop
  the whole host under .NET's default `BackgroundServiceExceptionBehavior.StopHost`. The
  `DispatchProgress` publish is isolated from the outcome-determining try/catch (a notification
  failure must never revert an already-completed job), and terminal `Completed`/`Failed` writes
  use `CancellationToken.None` so an in-flight outcome survives shutdown instead of being
  misdiagnosed as a crash on next startup. `ILogger<DispatchWorker>` logs full exceptions;
  `LastError` itself stays a concise, truncated (2000 char) message for the organizer-facing
  surface. Jobs dispatch to whichever `IDispatchJobHandler` is registered for their
  `DispatchJobType`; **none are registered yet** — the first are T041 (`SendInvitation`) and T075
  (`GeneratePdfs`/`BundleZip`/`SendResultEmail`), so a job would currently fail immediately with
  "no handler registered" if one were ever enqueued. `AddSignalR().AddJsonProtocol(...)` adds a
  `JsonStringEnumConverter`, so `DispatchProgress`'s `status`/`jobType` (and every future event's
  enum fields) serialize as their name, not the `System.Text.Json` default int (ADR-0007) —
  mirrors the DB-level string-enum convention (ADR-0004). **Known gap**: same pattern as
  `Realtime/` — `DispatchJobQueue`'s insert and `DispatchWorker`'s DB-backed sweep/dispatch loop
  still have no integration test; the harness they need now exists (T018) but no task has written
  this coverage yet. Only `DispatchRetryPolicy` (pure) is unit-tested now.
- **`BirraPoint.ServiceDefaults`**: OpenTelemetry (ASP.NET Core, HttpClient and runtime
  instrumentation; OTLP exporter switched by `OTEL_EXPORTER_OTLP_ENDPOINT`), default health
  checks (`self`/`live`), HttpClient resilience handler + service discovery.
- **Key packages** (pinned in the csproj): MediatR **12.5.0** (never upgrade to 13+ — license,
  R-03), FluentValidation 12.1.1 + FluentValidation.DependencyInjectionExtensions 12.1.1 (T013 —
  a separate package from core FluentValidation; only supplies `AddValidatorsFromAssembly` etc.),
  Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2 + Microsoft.EntityFrameworkCore.Design 10.0.4
  (build-time only, T009), Microsoft.AspNetCore.Authentication.JwtBearer 10.0.9 (T011 — ships as a
  separate NuGet package, not part of the ASP.NET Core shared framework), ClosedXML 0.105.0,
  QuestPDF 2026.7.0 (requires `QuestPDF.Settings.License = LicenseType.Community` at startup —
  pending, Dispatch slice), MailKit 4.17.0, Microsoft.AspNetCore.OpenApi 10.0.10 (T017) with an
  explicit direct reference on Microsoft.OpenApi 2.11.0 (transitive default 2.0.0 carries a known
  high-severity advisory, GHSA-v5pm-xwqc-g5wc).
- **Test harnesses**: xUnit in both test projects; the integration project additionally carries
  Testcontainers.PostgreSql 4.13.0 + Microsoft.AspNetCore.Mvc.Testing. `Persistence/
  SchemaTests.cs` (T009) spins up a real `postgres:16` Testcontainer, applies the migration,
  and asserts the constraints above end-to-end. T011/T012 are unit-tested only (no business
  endpoint exists yet to exercise over HTTP): claims-transformation/`CurrentUser`/DI-wiring tests
  under `UnitTests/Common/Auth/`, exception-handler tests against a bare `DefaultHttpContext`
  under `UnitTests/Common/Errors/`. T015's `Realtime/` tests are hand-rolled fakes (no mocking
  library in this repo) implementing `IHubContext`/`IHubClients`/`IClientProxy` directly — same
  "real/fake collaborator over mock" style as the T011 auth tests. T016's `Common/Jobs/` tests
  cover only `DispatchRetryPolicy` (pure math) — `DispatchJobQueue`/`DispatchWorker` remain
  DB-backed with no integration coverage yet (see Known gaps above).
- **`IntegrationTests/TestHost/`** (T018): the HTTP-level harness. `ApiFactory :
  WebApplicationFactory<Program>` owns one dedicated `postgres:16` Testcontainer per test class
  (`IAsyncLifetime`, same one-container-per-class convention as `Persistence/PostgresFixture`),
  migrates it in `InitializeAsync`, then `ConfigureWebHost`s the real app onto it:
  `UseEnvironment("Testing")` (so the Development-only auto-migrate/`MapOpenApi` gates stay off —
  the factory migrates explicitly instead), an in-memory `ConnectionStrings:db` override, and a
  `PostConfigure<JwtBearerOptions>` that clears `Authority` and swaps in `TestJwtIssuer`'s static
  `TokenValidationParameters` so no real Keycloak discovery round-trip ever happens.
  `TestJwtIssuer.IssueToken(sub, email, roles)` signs HMAC-SHA256 JWTs with a fixed test-only key,
  embedding `realm_access` as raw JSON (`JsonClaimValueTypes.Json`) — the exact shape
  `KeycloakRolesClaimsTransformation` parses — so tokens exercise the real role-mapping path, not
  a bypass. Required a one-line addition to the API itself: `public partial class Program;` at
  the end of `Program.cs`, since a minimal-API top-level `Program` is otherwise implicitly
  internal and invisible to `WebApplicationFactory<Program>` in the test assembly.
  `Catalog/GetStylesTests.cs` is the first consumer: unauthenticated → `401`, authenticated (any
  role, per the fallback policy) → `200` with all 125 catalog rows in numeric category order and
  exactly the four contracted fields per row. `CompetitionHub` and `DispatchJobQueue`/
  `DispatchWorker` DB-backed coverage (the two gaps above) can now be built on this same harness,
  but neither has been written yet.
- **`IntegrationTests/Auth/`** (T021, T023): `AuthPolicyTests.cs` proves the deny-by-default
  fallback (`401`, real `GET /api/v1/styles`), the `ORGANIZER` role policy (`403`), and the
  owner-scoped-404 convention (`404`, never `403`, for a right-role/wrong-owner caller — no
  cross-owner existence leak, `rest-api.md`'s stated convention) over real HTTP, plus a `200`
  control case. This branch has no real `ORGANIZER`-only + owner-scoped REST endpoint yet (that
  only exists in the unmerged Phase 4 work), so `TestOnlyAuthorizationEndpoints.cs` supplies a
  diagnostic-only `IStartupFilter` mapping two endpoints under an unambiguous `/__test/` prefix —
  registered exclusively inside `AuthPolicyTests` via `ConfigureTestServices`, never shipped, never
  in `contracts/rest-api.md` — that exercise the exact same `"ORGANIZER"` policy + ownership check
  as `CompetitionHub.JoinCompetitionAsOrganizer`, but through ASP.NET Core's authorization
  middleware so 403 (wrong role) and 404 (right role, wrong owner) come back as distinguishable
  HTTP status codes (the hub throws the same `HubException` for both, which is exactly why it
  isn't reused for this test — **this does not close ADR-0006's hub-coverage gap**, see Recorded
  debt below). `JudgeResolverTests.cs` (T023) seeds `Judge` rows across two competitions sharing
  one email and asserts: cross-competition backfill, idempotent replay (an already-backfilled row
  ignores a later call with a different sub/name), an unmatched email returns an empty list, and a
  name-less call backfills `KeycloakUserId` while leaving `DisplayName` untouched.
- **`Features/Monitoring/`** (T068–T069, US9): `GetProgress.cs` and `GetEntryEvaluations.cs`, the
  organizer dashboard's two read models. `GetProgress` computes every table's completed/expected/
  percent via three `GroupBy` queries (constant round trips regardless of table count — originally
  a per-table loop, fixed to this shape on senior-code-reviewer's PR #24 finding since this feeds a
  live dashboard organizers reload during an active event). `GetEntryEvaluations` joins `Evaluation`
  to `Judge` (`AsNoTracking`, another PR #24 fix — a read-only audit view has no reason to track
  the joined entities) for display names and only computes a consolidated mean once the entry's
  table is `Closed` — the rounding is a small deliberate duplicate of `CloseTableRules.ComputeMean`
  (2 decimals, `AwayFromZero`) rather than a cross-feature-folder import, matching how
  `CorrectEvaluation.cs` already duplicates `SubmitEvaluationRules`' values instead of reaching into
  another slice — the same duplication convention `Features/Dispatch/GeneratePdfsHandler.cs` (below)
  reuses a third time. `Features/TastingOrder/FixOrder.cs` also gained a `PublishToOrganizersAsync`
  emit for `TableOrderFixed` (same payload as the pre-existing judge-group one) —
  contracts/signalr-hub.md had documented this row since Phase 8, but nothing emitted it there until
  now.
- **`Features/Dispatch/`** (T072–T076, US10, ADR-0010): the finalize→PDF→ZIP→email pipeline.
  `ChangeState.cs`'s existing `Finalized` transition (the `tables-still-open` gate has existed since
  T028 but had zero test coverage until this phase) now also enqueues one `GeneratePdfs`
  `DispatchJob` — FR-036's actual trigger. Three `IDispatchJobHandler`s chain by enqueueing the next
  stage on success: `GeneratePdfsHandler` renders a `ScoreSheetDocument` (QuestPDF, one page per
  beer entry — competition name, blind code, style, every judge's five section scores/comments/
  total, consolidated mean; deliberately no participant/beer name anywhere in the PDF content
  itself per R-14/BR-01) per beer entry into a new `GeneratedScoreSheet` row (upserted by
  `BeerEntryId`, `bytea`, ADR-0010); `BundleZipHandler` assembles an in-memory
  `System.IO.Compression.ZipArchive` at the FR-040 path (`DispatchPaths.ZipEntryPath` — a pure
  function, deliberately not sanitizing the competition name since a ZIP entry name has none of a
  filesystem path's reserved-character concerns) into a new `ResultsArchive` row (upserted by
  `CompetitionId`), then enqueues one `SendResultEmail` job **per participant** — the same
  one-job-per-recipient convention `SendInvitation` already established, which is also how FR-041's
  per-recipient status/retry works: each participant's send is tracked entirely via that
  `DispatchJob`'s own `Status`/`Attempts`/`LastError`, no separate email-status entity needed.
  `SendResultEmailHandler` attaches every PDF belonging to that participant's entries via a new
  `IEmailSender.SendWithAttachmentsAsync` (`EmailAttachment` record; the pre-existing `SendAsync`/
  `SendInvitationHandler` call site is untouched). Dispatch scope is **competition-wide, not
  table-wide** — every participant/entry in the competition gets a PDF/ZIP-folder/email, including
  an entry never assigned to any table (zero evaluations, still a valid — if empty — score sheet).
  `DispatchEndpoints.cs` maps `GET .../results/archive` (200 streams `ResultsArchive.ZipBytes` if
  present, else 202 reporting the `BundleZip` job's current status), `GET .../dispatch` (every
  `SendResultEmail` job for the competition, joined to `Participant` for email), and
  `POST .../dispatch/retries` (resets a targeted job to a fresh `Pending`/`Attempts: 0` — a manual
  retry gets its own full attempt budget rather than continuing an exhausted automatic one; no
  proactive worker wake-up, the existing 30s safety-net poll picks it up).

## Frontend (`frontend/`, Angular 20)

- Standalone components + Signals; PWA via `@angular/pwa` (`ngsw-worker.js` registered
  `registerWhenStable:30000`, enabled outside dev mode); Tailwind CSS v4 through the PostCSS
  plugin (`.postcssrc.json`); zone-based change detection for now (**zoneless under evaluation
  — ADR-0003**).
- **Feature-Sliced Design skeleton**: `src/app/core/` holds `auth/` (T019, expanded T024),
  `api/`, `realtime/`, `offline/` (T020); `src/app/features/` now holds its first slice, `auth/`
  (T024, two placeholder landing components — see below); `src/app/shared/` still empty
  (`.gitkeep`). Root component is a minimal accessible shell (h1 + `router-outlet`).
- **Dependencies**: Angular-lockstep packages pinned to the 20.x line (`@angular/cdk@^20.2`,
  `keycloak-angular@^20.1` — ADR-0002); independent: `keycloak-js@^26.2`, `dexie@^4.4`,
  `@microsoft/signalr@^10`, `tailwindcss@^4.3`. Dev-only (T020): `fake-indexeddb@^6.2` — jsdom has
  no IndexedDB implementation, so testing real Dexie CRUD under Jest needs it (Dexie's own
  recommended test companion); same category as Testcontainers on the backend, never shipped.
- **Bundle** (production build): initial total ~547.1 kB raw / ~136.5 kB transfer (was ~471.1 kB /
  ~120.4 kB before T053) — still comfortably within the ≤ 500 kB **gzip** budget (Principle IX,
  measured as transfer size), but T053 is the first phase to cross the Angular CLI's own raw-byte
  budget (`angular.json`, `maximumWarning: 500kB`), which now prints a build warning (not an
  error — `maximumError` is 1 MB). Driven by `@angular/cdk/drag-drop`'s `CdkTrapFocus` a11y module
  and `@microsoft/signalr` becoming reachable code for the first time (T020's `CompetitionHubService`
  was wired but unconsumed until now). Not addressed this phase — flagged in Recorded debt below
  since the gzip budget (the actual constitutional gate) is unaffected today, but the margin to the
  CLI warning threshold is gone.
- **`src/environments/environment.ts`** (T019): local-dev-only config — `keycloak: { url, realm:
  'birrapoint', clientId: 'birrapoint-spa' }` (matches `infra/keycloak/birrapoint-realm.json`)
  and `apiBaseUrl`, both the fixed Aspire local ports (CLAUDE.md §Commands). No dev/prod split or
  build `fileReplacements` yet — real per-environment values and any build-time swap arrive with
  Phase 16 (Bicep/nginx).
- **`core/auth/`** (T019): the Keycloak auth core, built on the modern `keycloak-angular` v19+
  API (`provideKeycloak`/`createAuthGuard`/`includeBearerTokenInterceptor`) — the older
  `KeycloakService`/class-guard/`KeycloakBearerInterceptor` APIs are deprecated and unused.
  - `keycloak.providers.ts`: `provideAppKeycloak()` — `initOptions: { onLoad: 'check-sso',
    pkceMethod: 'S256' }` (ADR-0012, 2026-08-02; previously `login-required`). `check-sso`
    silently detects an existing session without forcing one, which is what makes the public
    `/welcome` landing (`WelcomeComponent`, `features/auth/welcome/`) reachable by an
    unauthenticated caller at all — FR-001 now requires exactly that: a public landing with
    explicit sign-in/create-account actions, no competition data, and no forced pre-render
    redirect. Route-level guards (`role.guard.ts` below) carry the actual access control that
    `login-required` used to provide implicitly; an unauthenticated caller hitting a guarded
    route falls through to `/` (the landing) rather than being blocked before Angular even
    bootstraps. `features: [withAutoRefreshToken()]` gives silent token refresh driven by user
    activity (R-11). **Library quirk worked around here**:
    `keycloak-angular@20.1.0`'s `AutoRefreshTokenService` and `UserActivityService` are plain
    `@Injectable()` with no `providedIn: 'root'`, so `withAutoRefreshToken`'s
    `inject(AutoRefreshTokenService)` throws `NG0201` unless both are also passed through
    `provideKeycloak`'s own `providers` array — done here. Caught only by a real browser check
    (Jest/jsdom never instantiates the Keycloak adapter far enough to hit it); re-check on any
    future `keycloak-angular` upgrade.
  - `auth-interceptor.providers.ts`: `provideAuthBearerInterceptor()` — the app's first
    `provideHttpClient()` registration, wired to the official `includeBearerTokenInterceptor`
    (no hand-rolled token code, Principle VII) scoped via `INCLUDE_BEARER_TOKEN_INTERCEPTOR_CONFIG`
    to a regex-escaped `environment.apiBaseUrl` only — the token is never attached to third-party
    requests.
  - `role.guard.ts`: `organizerGuard`/`judgeGuard` (`CanActivateFn` via `createAuthGuard`), each
    wrapping a directly-unit-testable predicate (`isOrganizerAllowed`/`isJudgeAllowed`) that
    checks `authData.grantedRoles.realmRoles`. Since ADR-0012's `check-sso` switch no longer
    guarantees authentication before a guard runs, these carry the real access-control weight now
    (previously they only branched on role, on the assumption `login-required` had already
    blocked anonymous access) — an unauthenticated caller simply has empty `grantedRoles`, so
    `hasRealmRole` is `false` for both and the caller falls through to the same landing-resolution
    fallback as a role mismatch. **T024**: a mismatch (or anonymous caller) redirects to the
    caller's *own* role landing via `role-landing.ts`'s `resolveRoleLandingUrlTree(authData)`
    (e.g. a JUDGE hitting `/organizer/**` lands on `/judge/tables`, not a dead end at root) —
    `parseUrl('/')` (the public landing) is the fallback for a caller holding neither role,
    including an anonymous one.
  - `role-landing.ts` (T024, new): `resolveRoleLandingUrlTree(authData): UrlTree | null` — the
    single ORGANIZER → `/organizer/dashboard`, JUDGE → `/judge/tables` mapping, shared by
    `role.guard.ts`'s mismatch branch above and `home-redirect.guard.ts` below (ORGANIZER wins if
    a caller somehow holds both roles).
  - `home-redirect.guard.ts` (T024, new): `homeRedirectGuard`, the `canActivate` for `''` —
    resolves to the caller's role landing when one exists, else `true` (falls through to render
    `AuthPlaceholderComponent`).
  - `auth-placeholder.component.ts`: **T024** repurposed this from "temporary render target for
    all three routes" (T019) to the `''`-only fallback for a caller recognized by Keycloak but
    holding neither `ORGANIZER` nor `JUDGE` (shouldn't happen given the backend's deny-by-default
    policy, but the frontend still needs to render *something* rather than loop) — kept and
    relabelled rather than deleted/recreated, since it was already small and tested.
  - `app.routes.ts` (**T024**, restructured; **T029** adds the wizard children): `''` →
    `homeRedirectGuard` + `AuthPlaceholderComponent` (no-access fallback); `organizer` (→
    `organizerGuard`) nests `dashboard` (→ `OrganizerDashboardComponent`), `competitions/new` and
    `competitions/:id` (both → `CompetitionWizardComponent`, T029), and a `'' → redirectTo:
    'dashboard'` child; `judge` (→ `judgeGuard`) nests `tables` (→ `JudgeTablesComponent`) the same
    way; `**` still → `''`. `canActivate` on the parent path segment already gates every
    descendant path in Angular's router, so no `canActivateChild` is needed — the wizard routes
    inherit `organizerGuard` from the parent without repeating it. Matches the
    CLAUDE.md-documented `/organizer/**`/`/judge/**` guard convention. `app.config.ts` still wires
    `provideAppKeycloak()` + `provideAuthBearerInterceptor()` (unchanged).
  - **`features/auth/`** (T024, new, first content in this previously-empty FSD layer):
    `OrganizerDashboardComponent` — standalone `OnPush` placeholder (`<h1>Organizer dashboard</h1>`)
    proving the routing/guard wiring for quickstart scenario 1; real content lands with US9 (Phase
    11). Its sibling, `JudgeTablesComponent`, served the same purpose for the JUDGE role until
    **T053** replaced it with real content — see `features/judge-tables/` below; `app.routes.ts`'s
    `judge` children now point at `JudgeTablesListComponent`/`JudgeTableOrderComponent` instead.
  - Verified against the full Aspire stack two ways: `frontend/e2e/us1-auth.spec.ts` (T022, below)
    driving a real Keycloak login end to end, and a manual browser check — unauthenticated visit
    to `/` → Keycloak-hosted login (PKCE `code_challenge_method=S256` visible) → seeded `organizer`
    login → `/organizer/dashboard` renders → `/judge` (same organizer session, no JUDGE role)
    redirects to `/judge/tables` → clean console throughout.
- **`features/competition-wizard/`** (T029, US2; step 3 added T105, FR-052): a 3-step organizer
  wizard — `BasicsStepComponent` (name/venue/startDate/endDate, the FR-007 required fields,
  `endDate >= startDate` cross-field validator, `Next` disabled until valid) →
  `DetailsStepComponent` (description/logoUrl/entryLimit/registrationStart/registrationEnd, all
  optional, `Save Draft` disabled until the entry-limit/registration-window validators pass) →
  `CategoriesStepComponent` (T105, terminal step — organizer-defined categories + BJCP style
  assignment, FR-052). Steps 1 and 2 both already had (T029) their own "Volver al listado" escape
  hatch: a hand-rolled `role="alertdialog"` + `cdkTrapFocus` confirm dialog (save-as-draft vs.
  discard, no CDK Dialog/Overlay service used — same inline pattern as the dashboard's advance-state
  confirm) rather than navigating away immediately; step 3 copies the identical pattern (its
  "Guardar y finalizar"/"Guardar borrador" both gate on `canFinish()` — at least one named category
  with at least one style assigned, since FR-052 makes the step mandatory). Because step 3 is now
  the terminal step, `DetailsStepComponent`'s save action was reverted back to emit-and-let-parent-
  advance (it had briefly navigated straight to the dashboard on save, before step 3 existed);
  `CategoriesStepComponent` owns the dashboard-navigation responsibility now.
  `CompetitionWizardComponent` (`competition-wizard.component.ts`) drives the step switch off a
  `currentStep` signal (widened `1 | 2 | 3`) and owns the create-vs-resume branch: with no `:id`
  route param it starts at step 1 with an empty form; with one, its constructor calls
  `CompetitionsApiService.getById` and passes the result down as `initialValue` to steps 1/2 (each
  has its own `effect()` that `patchValue`s the form when that input arrives) — step 3 instead
  fetches its own data (`CatalogApiService.getStyles()` + `CompetitionsApiService.getCategories()`)
  in `ngOnInit`, since a category list isn't part of `CompetitionDetail`. Deliberately **no
  client-side draft store** —
  `Competition.State` already defaults to `Draft` server-side (`Domain/Competition.cs`), so step
  1's "Next" (`create`/`update` via `CompetitionsApiService`) *is* the save point FR-008 asks for;
  `Location.replaceState` swaps the URL from `/organizer/competitions/new` to
  `/organizer/competitions/{id}` right after creation without a router navigation (which would
  recreate the component and lose `currentStep`/in-flight state, since `/new` and `/:id` are
  different route entries) — a plain page reload at that point already resumes correctly, purely
  because the browser URL now carries the real id. `CompetitionsApiService`
  (originally `competitions-api.service.ts` here; relocated to `core/api/` at T102 once
  `features/dashboard/` became a second consumer — see that section below) is a thin `ApiClient`
  wrapper (`create`/`update`/`getById`, `list`/`changeState` added later for US13) typed against
  `contracts/rest-api.md` §Competitions' exact wire shape (`CompetitionPayload`/`CompetitionDetail`).
  Both step components follow the same shape: a
  `ReactiveFormsModule` form, an `ApiError`-typed error signal split into per-field (`fieldError`)
  and banner (`bannerError`) display, and an `input.required<string>()`/`input<T | null>()` +
  `output<CompetitionDetail>()` contract so the parent wizard never reaches into child state
  directly.
  - **Click-to-jump stepper navigation (FR-007 amendment)**: the stepper's three markers are now
    `<button>`s (`canJumpTo(step)`/`goToStep(step)` on `CompetitionWizardComponent`) instead of
    inert `<span>`s. Step 1 is always reachable; steps 2/3 are gated on `competitionId() !== null`
    (they render `DetailsStepComponent`/`CategoriesStepComponent`, both of which declare
    `competitionId = input.required<string>()` and simply cannot render without one) — so jumping
    ahead is only possible once the competition has been saved at least once, which for an existing
    (edit) competition is immediately on load. Jumping away from a step discards unsaved in-step
    edits with no confirm dialog, identical to the pre-existing "← Volver" behavior. The active
    button carries `aria-current="step"`.
  - **Bulk BJCP-group style assignment (FR-052 amendment)**: each `<fieldset class="style-group">`
    in `CategoriesStepComponent` (one per BJCP catalog category, e.g. "Standard American Beer") now
    has a group-level `<select>` above its per-style rows; choosing an organizer category there
    calls `onBulkAssignGroup`, which loops `onAssignStyle` over every style in that BJCP group —
    reusing the existing single-style assignment path rather than duplicating its
    remove-from-all-rows-then-add-to-target logic. This overwrites any of the group's styles that
    were already individually assigned elsewhere (confirmed product behavior: the group selector
    always wins). The control is a stateless one-shot action, not bound state — it resets to its
    placeholder option after firing, since a group's own styles can be split across categories and
    have no single "current value" to display.
  - **Bug fix — per-style category selects showing "Sin asignar" for previously-saved
    assignments**: reported as "the assignment isn't kept after editing again". Root-caused with
    the running Aspire stack (real Postgres query on the reporter's own test competition + Chrome
    devtools inspection of the live component's signals), not just static reading — the DB and
    `CategoriesStepComponent.categories()` both held the correct data the whole time; only the
    rendered `<select>` was wrong. Cause: each per-style `<select class="style-row__select">` set
    its selection via `[value]="styleSelectValue(style.code)"` on the `<select>` host element,
    while the matching `<option>` came from a nested `@for` over `categories()`. On the style
    step's first render (right after the async catalog+categories load resolves), Angular applies
    the host's `[value]` binding before that `@for`'s `<option>` elements exist in the DOM, so the
    browser silently fails to select anything; since `styleSelectValue()`'s result never changes
    afterward, Angular's change-detection dirty-check skips re-applying the (unchanged) binding
    forever, leaving the select stuck on "Sin asignar" even though the model was always correct.
    Fixed by moving the selection onto each `<option>`'s own `[selected]="styleCategoryIndex(style.code) === ci"`
    binding instead (each option's binding runs against an element that already exists, so it isn't
    subject to the same host-vs-children ordering issue). Reproduced first in Jest (jsdom hits the
    same ordering quirk) with a new regression test asserting the rendered `<select>`'s own
    `.value`/`.selectedOptions`, not just the underlying signals — the prior bulk-assign tests only
    checked `categories()`/`styleSelectValue()`, which is exactly why they didn't already catch
    this. While auditing the same `--color-bp-*` design-token pattern that turned out to matter
    here, also found and fixed two more silently-broken tokens referenced by CSS but never defined
    in `styles.css`: `--color-bp-exito-600` (made the wizard stepper's own "done" checkmark circle
    invisible — white check on a background that resolved to transparent) and
    `--color-bp-info-600` (same latent gap, on the dashboard's "In Evaluation" status badge).
  - **Step 4 — ACCE-format entry import (ADR 0011, replaces the old `features/entry-import/`
    standalone screen entirely)**: `ImportStepComponent`
    (`steps/import-step.component.ts`) is now the wizard's terminal step, reached once
    `CategoriesStepComponent`'s "Continuar" (renamed from "Guardar y finalizar" — that step is no
    longer terminal, its `onFinish()` now `saved.emit()`s and lets the wizard advance instead of
    navigating away itself) advances `currentStep` to 4; step 3 now gets the same `is-done`
    checkmark treatment as steps 1–2. `canJumpTo(4)` uses the same `competitionId() !== null` rule
    as steps 2/3 — no extra "categories must exist" wizard-level gate; the step itself shows an
    inline `bp-alert` and disables upload when the competition has zero categories yet.
    - Upload → row list → per-row edit → consolidate, all against the rewritten
      `Features/Import/` backend contract (`core/api/import-api.service.ts`): every ACCE column
      (participant contact/ACCE#/DOB/phone, category, style, ABV, brew/bottling dates,
      malts/hops/yeast/other, entry instructions, organizer-typed beer name) is editable per row,
      not just style as before. Row editing reuses `CategoriesStepComponent`'s exact
      `editingIndex` expand/collapse pattern rather than a wide table — click "Editar" to expand a
      full field form, "Guardar fila" PUTs the complete row (full-replace, same convention as
      `UpdateCompetition`/`SetCompetitionCategories`), "Excluir" is a separate one-way action.
      Category correction is a plain `<select>` (a competition typically has a handful of
      categories, unlike the ~100+ BJCP styles that justify `StylePickerComponent`, still reused
      here for style correction). One wire-shape asymmetry worth remembering:
      `ImportRowDataDto`'s resolved style field serializes as `resolvedStyleCode` (ASP.NET
      camelCase only lowercases the first letter of `ResolvedStyleCode`), while the *request* body
      field for the same concept is `styleCode` — the TS types in `import-api.service.ts` encode
      this asymmetry explicitly rather than reusing one interface for both directions.
    - Consolidate no longer auto-navigates on success. It shows the "Imported: N. Excluded: N."
      summary and waits for an explicit "Ir al panel de organizador" button — an earlier version
      set the result signal and called `router.navigateByUrl` in the same tick, so the summary was
      never actually visible before the redirect fired (a self-contradictory instruction in the
      build brief: "show the summary" + "navigate like step 3's old terminal behavior", which
      doesn't compose). Fixed post-hoc with its own regression test asserting the confirm button
      appears and no navigation happens until it's clicked.
    - **Bug found and fixed during live verification, not by any test**: `ImportStepComponent`'s
      `imports` array initially omitted `FormsModule` — the *exact* regression already documented
      above for the old `entry-import.component.ts` (T036/T037): without it, `<form
      (ngSubmit)="onUpload()">` has no `NgForm` directive to intercept the native `submit` event,
      so clicking "Subir archivo" does a real browser form submission — full page reload,
      re-triggering this app's Keycloak `login-required` OIDC redirect and wiping all wizard
      state. It looked exactly like a Keycloak token-expiry race at first (the URL grows a
      `#state=...&code=...` fragment after every attempt) until cross-referencing this very
      changelog entry made the actual cause obvious. Invisible to Jest for the identical reason as
      the original 2026 bug: the existing spec called `onUpload()` directly, never dispatching a
      real `submit` event on the `<form>` element. Fixed by adding `FormsModule`; ported the exact
      same regression test pattern (`form.dispatchEvent(new Event('submit', ...))`) that caught it
      the first time. Verified end-to-end afterward against the live Aspire stack with a real
      generated `.xlsx` (the organizer's own example data): upload → StyleMismatch/CategoryMismatch
      correction → consolidate produced exactly 5 `BeerEntry` rows with correct blind
      codes/styles/categories/ABV, `BeerName` correctly `null`, `EntryInstructions` present only on
      the rows that had it, and exactly 2 deduplicated `Participant` rows with every new field
      populated.
    - **T109 (FR-054)**: the wizard's `@switch (currentStep())` destroys/recreates the non-active
      step's component instance, so `ImportStepComponent`'s local signal state used to reset every
      time the organizer left step 4 (e.g. "← Volver" to step 3 to fix a category/style assignment)
      and came back — the pending import looked gone, forcing a re-upload. Fixed by hoisting just
      the pending batch's `importId` (not the whole batch — the row data must be refetched anyway
      since categories may have changed) onto `CompetitionWizardComponent` itself as a signal,
      which — unlike its `@switch`-managed children — is never destroyed by step navigation; passed
      down as `[importId]`/`(importIdChange)`. On mount with a non-null `importId` input,
      `ImportStepComponent` calls the new `ImportApiService.revalidate()` instead of showing the
      upload form, so returning to step 4 both restores and re-checks the import in one call. Also
      fixed: `row.error` (the specific per-row failure reason, always returned by the API) was
      computed server-side but never rendered — the screen only ever showed the coarse status
      badge. Now shown inline per unresolved row. Deliberately out of scope: surviving a full page
      reload (the wizard already resets to step 1 on reload for all four steps today, a pre-existing
      gap this task didn't extend or fix) and filtering the style picker to only a row's resolved
      category's assigned styles (kept showing the full BJCP catalog, per FR-011's existing
      "searchable catalog list" wording).
    - **FR-007 (2026-08-01)**: leaving a step with unsaved edits via "← Volver" or a direct
      stepper-number click used to discard them silently — `@switch` destroys the leaving step's
      component instance the same way T109 above describes. Each of the four step components now
      reports its own dirty state upward via `dirtyChange: OutputEmitterRef<boolean>`: `basics-`/
      `details-step` subscribe to `form.valueChanges` and emit `form.dirty` (Reactive Forms doesn't
      mark a control dirty from a programmatic `patchValue`, only from real user input, so the
      initial data load never falsely reports dirty); `categories-step` has no `ReactiveFormsModule`
      form to key off, so it snapshots `JSON.stringify(categories())` right after the initial load
      and diffs against that; `import-step` counts an open row editor (`editingIndex() !== null`)
      or a chosen-but-not-yet-uploaded file as dirty. `CompetitionWizardComponent` aggregates
      whichever child is mounted into one `stepDirty` signal and routes every step-to-step
      navigation (`onBack`, stepper clicks) through a shared `attemptNavigate(step)`: clean → same
      immediate `currentStep.set(step)` as before; dirty → holds the target in `pendingStep` and
      shows a confirm dialog instead. Deliberately a **stay-or-discard** prompt, not save-or-discard
      — unlike the two existing `confirmingBack` dialogs in `basics-step`/`categories-step` (for
      leaving the wizard entirely to the dashboard, which already had a single "Guardar borrador"
      action to call), there's no one save action that composes across all four steps' differing
      save semantics, so the wizard-level dialog only offers "Seguir editando" (close, stay) or
      "Descartar y continuar" (navigate, discarding — the leaving component's local state is simply
      garbage-collected by `@switch`, same as any other navigation).
    - **Import step, "Excluir" always visible**: previously only reachable from inside the expanded
      per-row editor (`.import-row__editor-actions`); moved to the collapsed row summary next to
      "Editar" so excluding a row no longer requires opening it first. Surfaced a pre-existing gap
      while adding a distinguishing accessible name for the now-several-at-once "Excluir" buttons:
      `[attr.aria-label]` on a `<bp-button>` sets the attribute on the custom-element host tag, not
      on the native `<button>` it renders internally, so it never reached the accessible name —
      already latently true of the existing "Editar"/"Eliminar" buttons in this same file and in
      `categories-step`, just never exercised because there was previously only ever one of each on
      screen at a time needing disambiguation. Fixed at the source: `BpButtonComponent` gained an
      `ariaLabel` input that forwards onto the real `<button>`; the pre-existing "Editar"/"Eliminar"
      call sites were left on their old (currently-inert) `[attr.aria-label]` binding since fixing
      those wasn't part of this change.
    - **Sticky `.step-actions` bar**: all four steps' bottom action row (Back/Cancel + primary
      submit) is now `position: sticky; bottom: 0` with a solid background, so the organizer doesn't
      have to scroll a long step (`categories-step` especially, with many BJCP styles listed) to
      reach the save/continue action. Bleeds to the `wizard-card`'s own padded edges via negative
      margins (each step's own CSS, matching `wizard-card`'s `padding`/`--spacing-8`, with a
      `640px` breakpoint override matching the card's own responsive padding drop to `--spacing-6`)
      so the bar reads as part of the card rather than a gap-floating overlay. Pure CSS — not
      exercised by Jest/jsdom (no real layout engine), verified in a live browser instead.
  **Step 5 (T119, US14, undocumented until now)**: `JudgeImportStepComponent`
  (`steps/judge-import-step.component.ts`) — judge-roster `.xlsx` upload, structurally mirroring
  step 4's upload/row-list/per-row-edit/consolidate shape (`JudgeImportApiService` mirrors
  `ImportApiService`), reached once step 3's action advances `currentStep` to 5; a `judgeImportId`
  signal hoisted onto the wizard follows T109's exact survive-navigation pattern (no revalidate
  endpoint exists for judge imports, so returning to step 5 just re-fetches the batch's current
  state). No "notify judges" action here — that stays a separate post-wizard action on
  `features/judge-management/`'s roster table (T120), since bulk-inviting a whole roster isn't a
  step-5-local concern.
  **Step 6 (T123, added 2026-08-04, FR-016)**: `TablesStepComponent`
  (`steps/tables-step.component.ts`) — table assignment (create tables, assign judges/beers, see
  balance stats), now the wizard's actual terminal step (superseding the "step 3/step 4 is
  terminal" framing in the bullets above — both were true only until the next step existed). Does
  **not** rebuild table-assignment logic: it embeds the already-fully-built US5 screen (see
  `features/table-management/` below) via a new route-agnostic `TableBoardComponent`, extracted
  from what used to be `table-management.component.ts` so the same board renders both inside this
  wizard step (`competitionId` passed as an `input()`) and unchanged at its original standalone
  route (`table-management.component.ts` is now a thin wrapper reading `ActivatedRoute` and
  passing the id down). `dirtyChange` always emits `false` here — every board mutation
  (create/drag-drop/click-to-detail "Move to") already saves immediately via the API, so there is
  never anything un-persisted to lose on navigation, unlike steps 1–5's form-based dirty tracking.
  Step 5's own terminal button changed from "Ir al panel de organizador" (which used to leave the
  wizard entirely — `goToDashboard()`/its `Router` injection removed, now dead) to "Continuar"
  (`saved` output added, advances the wizard to step 6 instead) — the dashboard-exit action moved
  to step 6's own "Ir al panel de organizador" button. Draft→Active activation deliberately
  **stays** a separate manual dashboard action (T102), not fused into finishing this step — the
  organizer may want to keep adjusting tables before activating.
- **`features/judge-management/`** (T043, US4): another single signal-driven container (no
  stepper) — a paste-list registration form (textarea split on newline or comma, trimmed, empties
  dropped) always shown together with a delivery-status table, since the two are meant to stay
  visible side by side rather than gated behind navigation. `JudgeManagementApiService` mirrors the
  same thin-`ApiClient`-wrapper shape as `EntryImportApiService`/`CompetitionsApiService`. The
  delivery-status table loads on component init (not just after a registration) and refreshes
  after every register/resend/edit action, so reopening the screen always reflects current state;
  each row carries `[attr.data-judge-email]` (same E2E-hook convention as `entry-import`'s
  `data-row-number`). Skip reasons are translated to plain language client-side
  (`duplicate-in-list` → "duplicate in the pasted list", `already-registered` → "already
  registered") rather than surfacing the raw API strings. Edit-email is inline per-row (toggle →
  `<input type="email">` + Save/Cancel) rather than a separate view, since it's a single-field
  correction; a `409 judge-already-active` failure surfaces through the same generic
  `ApiError.detail` path as any other error, no special-casing needed since the backend's detail
  message is already user-readable. A `busyJudgeId` signal disables the acting row's
  Resend/Save buttons during its own in-flight request without a full-page loading blocker. Route:
  `competitions/:id/judges` under `organizer`, same direct-navigation-only convention as
  `entry-import`.
- **`features/table-management/`** (T048/A/B/C, US5): the largest single-screen feature slice so
  far — an "Unassigned" source column (plain list, deliberately no seat/table iconography) plus
  one `MesaCardComponent` per table rendering the physical-table metaphor from the organizer's
  "Crear mesas" prototype: judges as seats positioned trigonometrically around an ellipse, beer
  tokens centered inside, per-table stats (`meanAbv`/`styleCount`/`styles`) read directly off the
  backend's own computed `stats` field, no client-side recomputation. `JudgeSeatComponent`
  (~38px)/`BeerTokenComponent` (~64px, T048C sizing) are shared draggable presentational items
  reused identically by both `MesaCard` and the Unassigned column, so click-vs-drag
  disambiguation and hit-area sizing live in exactly one place each. **`ClickVsDragDirective`**
  (T048B) is the click-vs-drag mechanism: tracks the `pointerdown` position, attaches a one-shot
  `window` `pointerup` listener, and fires its own `appClickVsDrag` output only if movement stayed
  within a 6px threshold — deliberately independent of CDK's own drag detection (never calls
  `preventDefault`/`stopPropagation`), verified in a real browser (not just Jest, jsdom can't
  simulate pointer capture/movement reliably for this) that a plain click on an already-seated
  item opens its detail modal while a real drag does not, and that disambiguation doesn't get
  "stuck" on the item a drag just dropped. This is the **first use of `@angular/cdk/drag-drop`**
  in this codebase — Phase 8/T053 (order reordering, `features/judge-tables/`) was the second,
  following this pattern rather than the other way around (the original task text had the dependency
  backwards). `TableDetailModalComponent` (T048A) shows only fields the backend actually has —
  beer: blind code, style name, ABV range, assigned table; judge: name, email, assigned table —
  per the user-approved scope cut (no allergen/award/certification, see Global status above); it
  also doubles as **T048B's mandated keyboard-accessible drag-drop equivalent**, not called out in
  the task text but required by this codebase's accessibility mandate (Principle VIII, "every drag
  & drop has a keyboard-accessible equivalent") — a "Move to" `<select>` + button reachable via
  Enter/Space on any seat/token, calling the exact same mutation path as a real drag. Every
  judge/beer move (drag or keyboard) goes through `TableManagementComponent`'s `moveJudge`/
  `moveBeer`, which issue one or two sequential `PUT /tables/{id}` calls (remove-from-source then
  add-to-target for a cross-table move) and only update local state once each call actually
  resolves — a mid-flight failure can never leave local state ahead of what the server committed.
  The COI conflict dialog resolves the `409`'s top-level `conflicts` field (confirmed via
  `DomainExceptionHandler.cs` that `DomainException.Extensions` serialize as flat top-level
  ProblemDetails members, not nested — `ApiError.extensions['conflicts']`, not
  `extensions.extensions.conflicts`) into judge display names / entry blind codes for a readable
  message; it gained `cdkTrapFocus`/`cdkTrapFocusAutoCapture` in the same PR #19 review pass that
  found the backend BOS-unflag bug above — the detail modal already had it, the conflict dialog
  initially didn't, so a keyboard user landed with focus nowhere in particular after a rejected
  move. **Bug found by its own E2E spec, fixed same-day**: the mutation-response reconciliation
  originally patched the `entries` signal's `tastingTableId`/`notValidForBos` incrementally from
  only the mutated table's own membership — but `bosFlaggedEntryIds` only ever reports newly
  *flagged* ids (never unflagged ones) and FR-018 can flag/unflag entries anywhere in the
  competition, not just at the table being edited, so an entry's BOS-flagged visual state could go
  stale outside the table just mutated until a full page reload. Fixed by refetching `GET
  /entries` wholesale after every mutation instead of patching incrementally — simpler and
  correct in both directions, at the cost of one extra request per mutation (acceptable for an
  organizer-only setup screen). Route: `competitions/:id/tables` under `organizer`, same
  direct-navigation-only convention as the other two Phase 5–6 feature routes.
  **T122/T123 (2026-08-04, FR-016)**: this screen is now reachable two ways — unchanged at its
  original standalone route (post-activation editing) and as the wizard's new step 6 (Draft-time
  setup, see `features/competition-wizard/` above) — via a straight logic extraction, not a
  rewrite: everything that used to live in `table-management.component.ts` (every signal,
  computed, drag-drop/click-to-detail handler, the BOS banner and COI dialog) moved verbatim into
  a new `TableBoardComponent`, whose only real change is taking `competitionId` as an
  `input.required<string>()` instead of reading it off `ActivatedRoute` — which surfaced a genuine
  Angular constraint: the old constructor called `loadAll()` synchronously, but a required signal
  input is only guaranteed populated from `ngOnInit()` onward (`NG0950` otherwise), so the initial
  load moved there. `table-management.component.ts` is now an eight-line wrapper. The backend's
  `TableStatsDto.MeanAbv` was also found wrong while wiring this up and fixed as part of the same
  change: it averaged the *BJCP style's* declared ABV-range midpoint (`Features/Tables/TableProjector.cs`),
  not each beer's own submitted `AbvPercent` — silently dropping any table seated with a style that
  has no declared range (confirmed with a real one, `27-Kellerbier`, in the seeded catalog) out of
  the average entirely. `TableSampleDto`/`EntryDto` gained a real `AbvPercent` field
  (`contracts/rest-api.md` updated) and `MeanAbv` now averages that directly — null only when a
  table has zero samples. `MesaCardComponent` also gained live judge-count/beer-count stats,
  `UnassignedColumnComponent`'s headers show live unassigned counts, and
  `TableDetailModalComponent`'s beer view shows the real ABV% alongside the style's declared range
  — all sourced from data already on the wire, no new endpoints. Presentational-only pass across
  `mesa-card`/`judge-seat`/`beer-token`/`unassigned-column`/the "Add table" form: raw hex colors
  and native `<input>`/`<button>` replaced with this codebase's `--color-bp-*` design tokens and
  `bp-button`/`bp-input` components (matching the wizard steps' own established look), with every
  `data-*`/`aria-label`/`role` attribute the 8 dependent E2E specs (`us5`–`us13`) and the a11y
  sweep lock onto preserved byte-for-byte — confirmed by inspection rather than a green E2E run:
  `us5-tables.spec.ts`'s shared login `beforeEach` never got past the app's own landing page to
  reach Keycloak at all, a pre-existing condition traced (via `git log`) to `welcome.component.ts`
  as last touched by an earlier commit on this same branch, before this change and unrelated to
  anything touched here.
  **Usability redesign (2026-08-05, organizer feedback)**: the trigonometric-ellipse "physical
  table" board (judges as seats orbiting a circle, beer tokens centered inside) required opening
  each seat/token's detail modal to see who or what was actually assigned — fine for a fully-staffed
  table, but slow for the organizer's *first-pass* assignment across many tables, and the tall
  circle ate vertical space long before the row got interesting. `MesaCardComponent` and
  `UnassignedColumnComponent` replaced it with a two-column roster: judges as a vertical list
  (avatar + full name + email) and beers as a wrapped grid of chips (blind-code token + style name
  + ABV%), all readable without a click — `JudgeSeatComponent`/`BeerTokenComponent` themselves are
  untouched (still the actual `cdkDrag` handles, same aria-labels/`data-*` hooks the E2E suite locks
  onto), just laid out differently by their parents. `seatPosition()`/`SeatPosition` (the ellipse-
  placement math) and the circular `.mesa-board` are gone as dead code. `TableBoardComponent`
  wraps the per-table cards in a `.mesa-grid` (`grid-template-columns: repeat(auto-fit,
  minmax(360px, 1fr))`) beside a `position: sticky` Unassigned column, so multiple tables lay out
  side by side on a wide screen instead of stacking into one long column — reachable width was the
  actual blocker: `competition-wizard.component.ts`'s `.wizard-container` caps every step at `40rem`
  (a deliberate reading-width constraint for the name/venue/date-style steps), which left this grid
  no room to ever produce more than one column. A `wizard-container--wide` modifier (`75rem`),
  applied only `[class.wizard-container--wide]="currentStep() === 6"`, opens up step 6 alone; the
  five form-style steps keep their original width. All existing `mesa-seats`/`mesa-tokens`
  id/class hooks, `data-judge-id`/`data-entry-id`/`data-table-id`, and the `beer-token--bos-flagged`
  class survive unchanged — verified against the full Jest suite (still 594/594 passing) and by
  manually driving the redesigned board in a real browser (Playwright's `us5-tables.spec.ts` itself
  still can't run, blocked by the pre-existing login `beforeEach` issue noted just above).
- **`features/judge-tables/`** (T053, US6): the JUDGE role's first real screen —
  `JudgeTablesListComponent` (route `/judge/tables`, the post-login landing) lists assigned tables
  with an order-fixed badge; `JudgeTableOrderComponent` (route `/judge/tables/:tableId`) is the
  blind sample/order view. `TastingOrderApiService` mirrors the established thin-`ApiClient`-wrapper
  shape. Renders exclusively `blindCode`/`styleCode`/`styleName` from `JudgeSample` — the BR-01
  boundary carried through to the template, not just the DTO. Reorder before fixing: CDK
  `cdkDropList`/`cdkDrag` with a dedicated `cdkDragHandle` (not the whole row, since — unlike
  `MesaCard`'s tokens — each row here already has two independent interactive buttons nested
  inside it, so the drag surface is restricted rather than resolved via `ClickVsDragDirective`'s
  pointer-threshold heuristic) plus per-row keyboard Move up/down buttons, the FR-020 equivalent
  this codebase's accessibility mandate requires alongside every drag gesture, not after it. "Fix
  order" is a one-shot, irreversible action gated by an explicit `role="alertdialog"` confirm step
  (`cdkTrapFocus`, learned from Phase 7's PR #19 review finding on the COI conflict dialog — applied
  here from the start rather than added after the fact). Connects to `CompetitionHubService`,
  `joinTable`/`leaveTable` on init/destroy, and subscribes to `TableOrderFixed` filtered to the open
  table — receiving it live-patches `sequenceOrder`s and flips the locked state without a refetch;
  losing the one-shot race (`409 order-already-fixed`) reconciles by refetching
  `GET .../samples` rather than trusting the pre-race local reorder. Realtime is treated as
  best-effort throughout (a hub connection failure leaves the view fully functional over REST, just
  without live updates until next load) — contracts/signalr-hub.md's own framing, "events are
  notifications, not the source of truth."
- **`features/dashboard/`** (T100, US13): `OrganizerDashboardComponent` — the real ORGANIZER
  landing page (route `/organizer/dashboard`), replacing T024's `<h1>Organizer dashboard</h1>`
  placeholder in `features/auth/` (deleted, same stub-removal convention as `judge-tables`'s T053).
  Loads `CompetitionsApiService.list()` (new method, `GET /competitions`) and renders each owned
  competition's name/venue/dates plus a `badge--{state-lowercased}` pill; each row is a
  `routerLink` to the wizard (`/organizer/competitions/{id}`) for `Draft`, the tables screen
  (`/organizer/competitions/{id}/tables`) for `Active` (still the setup/assignment view — there's
  nothing left to configure once evaluation has started), or the live monitoring dashboard
  (`/organizer/competitions/{id}/monitor`, T070/US9, see below) for `InEvaluation`/`Finalized` — the
  original placeholder "everything past Draft goes to tables" stand-in from T100 is now resolved.
  An always-visible "New competition" action routes to
  `/organizer/competitions/new`; zero competitions renders an empty state with the same CTA
  (FR-050, Acceptance Scenario 4). Single component, no list/item split.
  **Bug found and fixed the same day, unrelated to this task's own files**: while visually
  verifying this component in a real browser (not just Jest/jsdom), the routed content rendered
  correctly but was pushed entirely below the fold — `frontend/src/app/app.html` (unchanged since
  T003/T004 scaffolding) wrapped a static, purely decorative `<h1>{{ title() }}</h1>` (`"BirraPoint"`)
  in `<main class="flex min-h-screen items-center justify-center …">`, permanently consuming the
  full viewport height ahead of `<router-outlet />` on **every** route in the app, not just this
  one. Invisible to every prior E2E spec since Playwright's locators don't care about scroll
  position. Fixed by removing the splash entirely (`App`'s `title` signal removed too, now unused);
  each routed page already renders its own `<h1>`, so this was dead weight, not a needed
  page-title slot — also resolves a latent multiple-`<h1>` accessibility smell.
  **T102 (2026-07-22)**: each row gains an advance-state `<button class="advance-state-action">`
  as a **sibling** of the navigation `<a>`, never nested inside it (a `<button>` inside an `<a>` is
  invalid HTML and a nested-interactive-control accessibility hazard) — shown only when a next
  state exists per FR-006's forward-only chain (`NEXT_STATE`/`ADVANCE_LABEL` lookup tables,
  `Finalized` renders none), behind the same explicit `alertdialog`+`cdkTrapFocus` confirm pattern
  `judge-table-order.component.ts`'s "Fix order" already established — irreversible actions get a
  confirm step in this codebase, consistently. Success refetches the list rather than mutating
  state locally (same reconciliation convention as `judge-table-order.component.ts`/
  `table-management.component.ts`); `409 tables-still-open` reads the `openTableIds` ProblemDetails
  extension array's length for the blocked-count message (resolving the ids to table names was
  scoped out — an extra round-trip not worth it for an error path); `409 invalid-state-transition`
  (a same-competition race) shows a plain message and refetches to reconcile.
  **T070 (US9)**: new sibling component `competition-monitor.component.ts` (route
  `/organizer/competitions/:id/monitor`, the routing target above for `InEvaluation`/`Finalized`).
  On init: one `forkJoin` of the competition header (`CompetitionsApiService.getById`), the initial
  `/progress` rows (`MonitoringApiService.getProgress`), and the competition's entries
  (`EntriesApiService.getEntries`, promoted out of `table-management` this same task since it's now
  a second consumer — same FSD rule as `CompetitionsApiService`) grouped client-side by
  `tastingTableId` to know which blind codes sit under each table row for the drill-down list. Then
  joins the `competition:{id}:organizers` SignalR group for the first time ever in this codebase
  (`CompetitionHubService.joinCompetitionAsOrganizer` existed since T020 but had no caller until
  now) and patches state in place from three live events rather than refetching:
  `EvaluationCompleted` replaces just the matching row's `{completed, expected, percent}` (FR-037's
  "no reload, no flicker"), `TableClosed` flips that row's badge to `Closed`, `TableOrderFixed`
  renders a per-table "Order fixed by {name}." note. Clicking a blind-code button calls
  `getEntryEvaluations(competitionId, entryId)` fresh every time (simpler than caching
  `TableClosed`'s `consolidatedScores` payload) and renders a read-only panel — every judge's five
  scores/comments as plain text, total, status, and the consolidated mean or "not yet closed" — with
  no form controls anywhere in it (FR-038 Acceptance Scenario 2, asserted directly in the E2E spec
  by counting `input`/`textarea` elements inside the drill-down section: zero).
  **T077 (US10)**: the header gains a "Results & Dispatch" link, shown only once
  `comp.state === 'Finalized'`, to `features/results-dispatch/` (below).
- **`features/evaluation-sheet/`** (T059/T060B, US7): the capped five-section blind evaluation
  sheet, route `/judge/tables/:tableId/samples/:beerEntryId`. Renders exclusively `blindCode`/
  `styleCode`/`styleName` (BR-01) — never touches Dexie or the network directly, delegating all
  persistence to `SyncService`. Each section (Aroma 12/Appearance 3/Flavor 20/Mouthfeel 5/Overall
  Impression 10 — the exact caps, not a shared placeholder number) pairs a capped numeric score
  input with a comment `<textarea>` showing a live remaining-characters-to-20 hint; the total is a
  read-only client-side sum for display only, never submitted (the server's computed `Total` is
  always authoritative). Submit is gated on the whole form being valid. On mount, hydrates from any
  existing `SyncService.loadDraft()` result before rendering, so a resumed (or offline-restarted)
  sheet never starts blank. Offline badge ("Offline mode — data protected locally") tracks the
  `online`/`offline` window events live, not a one-time check. **Bug found and fixed by T061's E2E
  spec**: a genuine offline restart re-fetches `GET .../samples` to redisplay the current sample's
  header, and that fetch's `ApiError.status === 0` (never reached the server) was falling straight
  into the generic load-error screen — hiding the form the Dexie-backed draft had already loaded
  correctly underneath. Fixed with a `localStorage`-backed (deliberately not Dexie — this is
  read-only display metadata, not offline-engine state R-08 needs to reconcile) last-known-good
  cache per `beerEntryId`, consulted only on a genuine connectivity failure; a real `404`/`403` the
  server actively returned still blocks the view exactly as before.
  `features/evaluation-sheet/style-reference/` (T060B) is a collapsible read-only panel showing the
  sample's declared style's BJCP guide description via the new `CatalogApiService.getStyleDetail`
  (below), a real `<button aria-expanded>` toggle (WCAG, Principle VIII — no click-div), and an
  in-memory per-code cache (not persistent — a session-long cache is enough to survive re-toggling
  or a mid-session connectivity drop once already loaded, without committing to a bigger
  offline-cache architecture for reference data that isn't itself part of the sync engine).
  `judge-tables/judge-table-order.component.ts` gained the entry point into this sheet (FR-022):
  once the order is fixed, the first `NotStarted` sample (by fixed sequence — a `computed()` over
  the same `samples()` signal the drag/reorder/hub-event paths already keep in order, no separate
  re-sort needed) shows an "Evaluate" link into the sheet above; any later `NotStarted` sample shows
  a `Locked` badge instead, and `Submitted`/`PendingConsensus` samples show their own read-only
  badges — a judge structurally cannot open a sample out of turn from the UI. T066 (US8) added the
  Close Table action to the same component: a `canCloseTable` computed gates the button on
  `orderFixed() && samples().every(s => s.evaluationStatus !== 'NotStarted')` (every sample at least
  submitted, not necessarily consensus-resolved — a `PendingConsensus` sample doesn't block closing,
  only an untouched one does); confirming reuses the same `role="alertdialog"` +
  `cdkTrapFocusAutoCapture` pattern as the Fix Order and advance-state dialogs rather than inventing
  a new one. The component subscribes to a live `TableClosed` hub event alongside its existing
  `TableOrderFixed` subscription and flips a `tableClosed` signal that renders a closed banner and
  disables further submissions client-side (the server-side guard is `SubmitEvaluation`'s existing
  table-open check — this is only the UI reflecting it promptly). `POST /me/tables/{tableId}/close`
  can come back `409` three ways, handled distinctly: `evaluations-incomplete` and `discrepancy-open`
  render inline with the missing blind codes/discrepant codes from the ProblemDetails extension
  data; `table-closed` (a race — another judge's close request landed first) is treated as a
  success, not an error, since the table is closed either way and that's what the caller wanted.
  **T082 (US11)** added a live open-discrepancy-count banner (a `forkJoin`'d `getDiscrepancies`
  call alongside the existing tables/samples fetch, kept current by a third hub subscription —
  merged `DiscrepancyRaised`/`DiscrepancyResolved`, filtered to this table, re-fetching the count
  rather than deriving it from the event payload) and a link out to the discrepancy page from the
  `discrepancy-open` branch above.
- **`features/discrepancy/`** (T082, US11): `discrepancy-api.service.ts` wraps `GET
  /me/tables/{tableId}/discrepancies` and `PUT /me/tables/{tableId}/evaluations/{evaluationId}` —
  deliberately not routed through `SyncService`'s Dexie outbox, since the spec frames this repair
  flow as inherently online-only ("shown to each involved judge as soon as they are next online").
  `discrepancy-alert.component.ts` (route `/judge/tables/:tableId/discrepancies`) lists open alerts
  involving the caller — blind code, a totals-comparison table marking the caller's own row — each
  with an "Adjust my evaluation" action revealing a duplicated copy of `evaluation-sheet.component.
  ts`'s five-section form (same caps/20-char minimum; duplicated rather than shared, matching the
  backend's own validator-duplication convention for two otherwise-unrelated slices). A resolved
  adjustment (`discrepancy: null` in the `PUT` response) swaps the card for a confirmation and drops
  it from the open list; a still-open one updates the totals table in place. Joins the table's
  SignalR group and re-fetches on `DiscrepancyRaised`/`DiscrepancyResolved`, same convention as the
  banner above. `evaluation-sheet.component.ts`'s locked-sample message now branches on
  `PendingConsensus` specifically, linking here instead of the generic "already evaluated" dead end.
- **`core/api/`** (T020): the typed HTTP client + ProblemDetails→UI error mapping.
  - `problem-details.model.ts`: `ProblemDetails`/`ValidationProblemDetails` interfaces plus
    `BIRRAPOINT_ERROR_URNS` — the 14 `urn:birrapoint:*` values from contracts/rest-api.md §Error
    catalog as a `const` array (single source of truth; `BirraPointErrorUrn` is derived from it,
    and `isBirraPointErrorUrn()` checks membership at runtime), mirroring the backend's
    `DomainErrorType` enum/`DomainErrorCatalog` pairing.
  - `api-error.ts`: `ApiError` (status/title/urn/detail/errors/extensions) + `toApiError(status,
    body)`. Never throws regardless of input shape: a recognized birrapoint urn maps to a
    structured error (validation's `errors` field map, a domain conflict's extension fields like
    `conflicts`/`fixedBy`); an unrecognized urn — confirmed the framework's own default auth
    challenge/forbid responses are ProblemDetails too, via `services.AddProblemDetails()`, just
    without a birrapoint `type` — falls back to a generic error built from whatever `title`/
    `detail` exist; a non-JSON or empty body (network failure, `status 0`) falls back to a fully
    generic error.
  - `api-client.service.ts`: `ApiClient`, `providedIn: 'root'` — `get/post/put/delete<T>(path,
    options?)` against `${environment.apiBaseUrl}/api/v1${path}`, catching `HttpErrorResponse` and
    rethrowing `ApiError` via the mapper above; `options.headers` is how `SyncService` attaches
    `X-Idempotency-Key` (T060) without reaching for raw `HttpClient`. No per-endpoint methods here
    by design — those live in per-service files below and in each feature, growing as slices land.
  - `competitions-api.service.ts` (relocated here at T102 once a second feature needed it) and
    `catalog-api.service.ts` (T060B, new — `getStyleDetail(code)` against `GET /styles/{code}`,
    with an in-memory per-code cache) are this folder's two cross-cutting, multi-feature-consumed
    API wrappers so far; every other feature's API service still lives inside that feature's own
    folder, consumed by only one caller — see the FSD note in Recorded debt history for when a
    service is expected to move here.
- **`core/realtime/`** (T020): the `CompetitionHub` client.
  - `competition-hub.events.ts`: TS payload interfaces for the 8 server→client events +
    `CompetitionHubServerEvents` (name → payload map), mirrored from contracts/signalr-hub.md.
    Where the judge-group and organizer-group variants of the same event differ (`TableClosed`,
    `JudgeRemoved`), the richer organizer shape is typed with the extra fields optional.
  - `competition-hub.service.ts`: `CompetitionHubService`, `providedIn: 'root'`. Connects to
    `${apiBaseUrl}/hubs/competition` with `accessTokenFactory: () => keycloak.token` (the live
    `Keycloak` instance is itself injectable — `provideKeycloak` registers `{ provide: Keycloak,
    useValue: keycloak }`, confirmed in the shipped bundle) and `.withAutomaticReconnect()`.
    `joinCompetitionAsOrganizer`/`joinTable`/`leaveTable`/`leaveCompetition` invoke the matching
    hub method and track membership in two `Set`s; `onreconnected` replays every currently-tracked
    group — "clients re-join their groups on `onreconnected`" per the contract. Generic typed
    `on<K extends keyof CompetitionHubServerEvents>(event): Observable<…>`. Re-fetching state
    after a reconnect (events are notifications, not the source of truth) is each feature's job,
    not this service's. The real `HubConnectionBuilder` chain sits behind an injectable
    `COMPETITION_HUB_CONNECTION_FACTORY` token so tests substitute a hand-rolled fake
    `HubConnection` — same "real/fake collaborator over mock" convention as the backend's
    T011/T015 tests, not a mocking library.
- **`core/offline/db.ts`** (T020): `BirraPointDb extends Dexie`, `version(1).stores({ drafts:
  'beerEntryId', outbox: 'idempotencyKey, tastingTableId' })` (data-model.md §Client-side stores).
  `DraftRow`/`OutboxRow` mirror the evaluation POST body shape (`scores`/`comments` sub-objects,
  contracts/rest-api.md) plus bookkeeping (`updatedAt` for drafts; `attempts`/`lastAttemptAt`/
  `lastError` on outbox rows for R-08's backoff). `tastingTableId` is a secondary index on
  `outbox`, not just embedded in the key string, because T087 needs "outbox items for this table"
  lookups when a judge is removed.
- **`core/offline/sync.service.ts`** (T057/T060, US7): the replay engine `db.ts` was scaffolded for
  back at T020 — `saveDraft` debounces to `drafts` within the 300ms bound (SC-003/FR-026: a
  per-`beerEntryId` timer coalesces rapid keystrokes into one write, not one per keystroke, while
  still landing inside the bound). `submit()` is durable-first: the outbox row is written — and
  must successfully persist — before anything else, so a submission survives a reload even if the
  network attempt never completes; offline, it resolves immediately with no network attempt at all.
  While online it *does* await one immediate send so a **definitive** rejection (`400`/`409` — a
  domain conflict the server actively refused, e.g. `out-of-sequence`) can be surfaced to the judge
  right away, while anything transient (no real connectivity despite a stale `navigator.onLine`,
  5xx, timeout) resolves exactly like the offline path — a flaky connection never blocks or errors
  the "submit" action, only a definitive server rejection does; the outbox row stays queued in
  either case; only a confirmed `200`/`201` clears it. **Fixed same-day (senior-code-reviewer, PR
  #22)**: `HttpClient` has no default request timeout, so a hung socket on a nominally-online
  connection would have hung `submit()` and left `replayOutbox()`'s reentrancy guard stuck
  indefinitely — a 15s RxJS `timeout()` on the submit/replay POST makes "timeout" above a real,
  enforced case, not just an aspirational one. `replayOutbox()` is the background sweep —
  triggered by the `window` `online` event, the service's own construction (this codebase's stand-in
  for "app start," since `SyncService` is `providedIn: 'root'` and nothing else needed a dedicated
  `APP_INITIALIZER`), and immediately after each `submit()` — applying capped exponential backoff
  (1s/2s/4s/…/60s, loosely mirroring the backend's `DispatchRetryPolicy` shape translated to the
  client) so a long offline stretch doesn't spin the network on reconnect. Deliberately **not** the
  Background Sync API (unsupported on iOS Safari, R-08's explicit constraint). A Dexie write that
  throws (quota exceeded, private-browsing storage restrictions) propagates to the caller rather
  than failing silently, per the spec edge case. **Recorded debt**: a row that hits a definitive
  rejection stays in the outbox and retries forever with capped backoff — correct for a rejection
  that might later resolve (e.g. `order-not-fixed`), but for one that structurally never will
  (`table-closed`, `invalid-state-transition` once the competition moves past `InEvaluation`) it
  silently retries indefinitely with no further judge-facing surfacing after the initial toast; see
  Recorded debt below.
- **`features/results-dispatch/`** (T077, US10): `ResultsDispatchComponent`, route
  `/organizer/competitions/:id/dispatch`, linked from `competition-monitor.component.ts`'s header
  once `comp.state === 'Finalized'`. No new "finalize" UI — that's just another transition through
  the existing T102 advance-state button; this screen is what an organizer reaches afterward. On
  init loads `GET .../dispatch` for the per-participant status table (`data-participant-id` rows,
  `.badge--{status}`, a `Retry` button only on `Failed` rows, a `Retry all failed` bulk action only
  when more than one row is `Failed`) and joins the organizer SignalR group to render a live
  `DispatchProgress`-driven pipeline-stage indicator. **Archive download** required a design choice:
  a plain `<a href>` wouldn't carry the bearer token (only `HttpClient` requests go through the auth
  interceptor), so `core/api/api-client.service.ts` gained `getBlob()` (`responseType: 'blob',
  observe: 'response'`, its own blob-aware error-body decoder since a 4xx/5xx body arrives as a
  `Blob` too, not parsed JSON) and the component drives the actual save via
  `URL.createObjectURL` + a synthetic anchor click. The `GET .../results/archive` `200`/`202`
  duality (ZIP bytes vs. `{status}` JSON) is modeled as a discriminated `ResultsArchiveResult` union
  rather than a thrown error for the "not ready" case, decoded via a new `blobToText()` helper
  (`FileReader`-based, not `Blob.prototype.text()` — this project's Jest jsdom `Blob` polyfill has
  no `.text()`/`.arrayBuffer()`, so the FileReader approach keeps blob-to-text reads identical
  under test and in a real browser). **Archive readiness** has no cheap standalone check in the
  contract (the archive endpoint IS the readiness check and the download, in one call) — inferred
  instead from an exact live `{jobType: 'BundleZip', status: 'Completed'}` event, or, for a page
  loaded after the pipeline already finished with no live event to catch, a documented conservative
  fallback proxy (every participant row reaching a terminal `Completed`/`Failed` status, since the
  pipeline order is strictly `GeneratePdfs → BundleZip → SendResultEmail`).

## Testing & quality gates

| Suite | Command | Current state |
|---|---|---|
| Backend unit + integration | `dotnet test backend/BirraPoint.sln` | green — 184 unit tests (was 179; +5 **T079** `Evaluations/DiscrepancyTests.cs`: pairwise >7 detection incl. the 3-judge "middle judge not involved" edge case, resolution when all totals converge) against smoke + T010 `BjcpStyleSeedDataTests` (5) + T011 `Common/Auth` (6) + T012 `Common/Errors` (6) + T013 `Common/Behaviors` (7) + T015 `Realtime` (4) + T016 `Common/Jobs` (10) + T021 `Auth` + T025 `Competitions/CompetitionValidatorsTests` (23) + T031 `Import/` (22) + T038 `Judges/` (15) + T045 `Tables/` (13) + T050 `TastingOrder/` (10) + T055 `Evaluations/SubmitEvaluationTests.cs` (37) + T062-T063 `Evaluations/CloseTableTests.cs` (9) + T072 `Dispatch/DispatchPathsTests.cs` (2); 157 integration tests (+6 **T080** `Evaluations/DiscrepancyApiTests.cs`: divergent submit → `PendingConsensus` + alert, the 3-judge outlier-only-involved case incl. asserting the uninvolved judge's own response carries `discrepancy: null`, `PUT` adjustment resolving an alert, `PUT` outside an open alert → `409 evaluation-locked`, `PUT` on someone else's evaluation → `404`, close blocked then succeeding after resolution — plus one pre-existing `CloseTableApiTests.cs` test's fixture adjusted from an 11-point judge score gap to 5, since it now trips the newly-active >7pt gate) against a real Testcontainers PostgreSQL: smoke + 6 schema tests (T009) + 5 catalog-seed tests (T010) + T014 `AuditWriterTests` (3) + T018 `Catalog/GetStylesTests` (2) + T021 `Auth/AuthPolicyTests` (4) + T023 `Auth/JudgeResolverTests` (4) + T026 `Competitions/CompetitionsApiTests` (14) + T032 `Import/ImportApiTests` (26) + T039 `Judges/JudgesApiTests` (16) + T046 `Tables/` (22) + T051 `TastingOrder/` (9) + T056 `Evaluations/SubmitEvaluationApiTests.cs` (12) + T057B `Catalog/GetStyleDetailTests.cs` (2) + T062-T065 `Evaluations/CloseTableApiTests.cs` (9) + T068 `Monitoring/MonitoringApiTests.cs` (8) + T073 `Dispatch/DispatchApiTests.cs` (6) |
| Frontend unit | `cd frontend && npx jest` | 481 tests across 52 suites total (6 pre-existing failures unrelated to this row — `keycloak.providers.spec.ts`/`bp-alert.component.spec.ts`/`welcome.component.spec.ts`, from untracked auth-flow work; everything below this point is green). +13 **T105**: `features/competition-wizard/steps/categories-step.component.spec.ts` (12, new wizard step 3) + 1 new `competition-wizard.component.spec.ts` case (2→3 step transition) — `T029 features/competition-wizard/` below is now 37, not 24 (intermediate deltas between T102 and T105 went untracked in this row). Historical breakdown (was 312 tests across 44 suites; was 290; +22 **T082**: `features/discrepancy/discrepancy-api.service.spec.ts` + `discrepancy-alert.component.spec.ts` (new, fake-collaborator style matching `judge-table-order.component.spec.ts`'s harness), plus new cases in `judge-table-order.component.spec.ts` (open-discrepancy banner + pluralization, hub re-fetch/filtering, the close-error link) and `evaluation-sheet.component.spec.ts` (the `PendingConsensus` branch); was 262; +28 **T077**: new `core/api/blob-text.spec.ts` (2), `core/api/dispatch-api.service.spec.ts` (5), `features/results-dispatch/results-dispatch.component.spec.ts` (15: status table rendering, retry/retry-all gating, download success/not-ready/error, live `DispatchProgress` pipeline-stage text), `core/api/api-client.service.spec.ts`'s `getBlob()` extension (3), plus `competition-monitor.component.spec.ts`'s new "Results & Dispatch" link coverage (2, shown/hidden by `Finalized` state)) against smoke (2) + T019 `core/auth` (10) + T020 `core/api` (8) + T020 `core/realtime` (5) + T020 `core/offline` (3) + T024 `core/auth`/`features/auth` (13) + T029 `features/competition-wizard/` (24) + T036 `features/entry-import/` (19) + T043 `features/judge-management/` (12) + T048 `features/table-management/` (46) + T053 `features/judge-tables/` (23) + T100/T102 `features/dashboard/` (19) + T057/T059/T060/T060B/T061 US7 work (48) + T066-T067 US8 work (13) + T070 US9 work (16). jest-preset-angular 17, jsdom, TS config via Node 24 native type stripping (no ts-node); Karma fully removed (R-13) |
| E2E + accessibility | `cd frontend && npm run e2e` (`playwright test -c e2e`) | **green — T089 closed the long-open a11y gap**: `us1-auth.spec.ts` through `us13-dashboard.spec.ts` (the full per-story set, unchanged) plus two new specs — **T089 `e2e/a11y/routes.a11y.spec.ts`** (a single journey test logging in as both organizer and judge and axe-scanning all 12 routes in `app.routes.ts`: every organizer screen (dashboard, wizard incl. import/judges/tables/monitor/dispatch) and every judge screen (tables list, order, evaluation sheet, discrepancies) — found and fixed 3 real WCAG 2.1 A/AA violations in `features/table-management/` (see Recorded debt below for detail), zero violations remain), and **T090 `e2e/us3-import-scale.spec.ts`** (SC-006: a 500-row `.xlsx` fixture with 100 rows/20% BJCP-style errors, hand-built as minimal real OOXML via `adm-zip` since no xlsx-writing library existed in this repo at this scale, resolves all 100 through the real per-row UI action and consolidates in one session — `Imported: 400 / Excluded: 100`). The old `smoke.spec.ts` and `e2e/a11y/home.a11y.spec.ts` (both dating to T004, pre-Keycloak) were **removed** — both asserted an unauthenticated `/` renders app content, which has been false since the auth guard landed; `us1-auth.spec.ts`'s existing redirect assertion and the new `routes.a11y.spec.ts` sweep already fully subsume what they were checking. All green against a live, fully-warmed Aspire stack. Chromium only |
| Performance & bundle budgets (Principle IX) | `k6 run infra/perf/api-budgets.js` · `cd frontend && npm run build:budget` | **T090/T091, new**. `infra/perf/api-budgets.js` (k6): read/write p95 thresholds (`<200ms`/`<500ms`) against representative endpoints; authored and validated for correctness but not run in CI yet — needs a pre-obtained ORGANIZER/JUDGE bearer token (neither Keycloak client here supports a non-interactive grant: `birrapoint-spa` has `directAccessGrantsEnabled: false`, the admin service-account token is Keycloak-Admin-API-scoped only) and the `k6` binary, which isn't installed in the dev sandbox this was authored in. Hardened after senior-code-reviewer findings on PR #28: an `http_req_failed` threshold plus `responseCallback: http.expectedStatuses(...)` on every request now fails the run loudly on a bad token instead of an expired/invalid token's fast 401s falsely satisfying both p95 budgets; a `setup()` guard refuses to run `writes()` at all unless `BEER_ENTRY_ID` already has a `Submitted`/`PendingConsensus` evaluation, since a first-time write would permanently lock a real evaluation (invariant 5/FR-035, no undo) — `writes()` therefore measures the idempotent-replay path, not a fresh insert, documented explicitly in the script's header rather than left implicit. Dashboard ≤1s and draft-save ≤300ms budgets are already covered — the former by `us9-dashboard.spec.ts`'s bounded-timeout live-update assertion, the latter more precisely by `sync.service.spec.ts`'s Jest fake-timer coverage than any E2E wall-clock wait could be. `frontend/scripts/check-bundle-budget.mjs` (new, zero new deps — only `node:zlib`): parses the built `index.html`'s initial `<script>`/`<link>` refs, gzips each for real, fails over 500 KB — `angular.json`'s own budget only checks *raw* bytes (typically 3-4x the gzipped size), so it stays as a cheap early warning while this script is the actual gate. Current real number: **202.03 kB gzip** (696.80 kB raw) — 60% under budget. The parsing itself was
hardened after a senior-code-reviewer finding (PR #28): the original fixed-attribute-order regexes
silently missed `<link href="…" rel="stylesheet">` (order flipped) and `rel="modulepreload"` initial
chunks — harmless only by coincidence, since this app currently has zero lazy routes — replaced with
an order-independent `readAttr()` helper that covers both. Lighthouse TTI (<3s/4G) was genuinely attempted (`npx lighthouse` ran real headless Chrome successfully) but is structurally blocked: this app has no anonymous route at all (`/` always redirects to Keycloak-hosted login before anything renders), so a single-navigation `lighthouse <url>` can never get past the redirect — a real measurement needs a Puppeteer-driven Lighthouse user-flow that logs in first, same as every E2E spec's `submitKeycloakLogin` helper; tracked as a small follow-up, not forced through here. |
| Lint / format | `ng lint` (angular-eslint flat config incl. template accessibility rules), `npm run format:check` (Prettier), `dotnet format --verify-no-changes` (backend/.editorconfig) | clean — T007 set Prettier `endOfLine: "auto"`: the gate had been red on every Windows checkout because git autocrlf smudges the tree to CRLF while Prettier defaults to `lf` |

## Data flows

`GET /api/v1/styles` (T017) is the first live REST endpoint: any authenticated caller (JWT bearer,
audience `birrapoint-api`) → `GetStylesQuery` via MediatR → `GetStylesQueryHandler` reads
`AppDbContext.BjcpStyles` → `200` with the lightweight `[{ code, name, categoryNumber,
categoryName }]` catalog projection. `CompetitionHub` (T015) is mapped at `/hubs/competition` and
accepts authenticated group joins; `DispatchWorker` (T016) is running and would emit
`DispatchProgress` on any job status change, but no story enqueues a `DispatchJob` yet. **T025–T028**
(US2): the organizer's `/organizer/competitions/new` → `CompetitionWizardComponent` → `POST
/competitions` (Draft by default) → `Location.replaceState` to `/organizer/competitions/{id}` →
Details step `PUT /competitions/{id}` ("Save Draft") is the first real read/write round-trip a
frontend feature slice makes against the backend (`ApiClient`, wired T020, first actually consumed
here via `CompetitionsApiService`); `ChangeCompetitionState` (T028) is the first real
`CompetitionHub` emitter, `CompetitionStateChanged` to the `competition:{id}:organizers` group —
though nothing subscribes to it yet (the dashboard that would, US9, is Phase 11). **T031–T037**
(US3): `/organizer/competitions/{id}/import` → `EntryImportComponent` → `POST
.../imports` (multipart `.xlsx`) → `WorkbookParser` validates row-by-row against
`BjcpStyles` → `201` with per-row statuses rendered inline; `StyleMismatch`/`Invalid` rows are
resolved via `PUT .../rows/{rowNumber}` (`assign-style` using the same `GET /styles` catalog the
picker filters over, or `exclude`) until none remain, then `POST .../consolidate` creates
`Participant`/`BeerEntry`/`EntryCollaborator` rows with generated blind codes — the first time
this repo's import staging model (`ImportBatch`/`ImportRow`) round-trips a file through to real
domain entities. **T038–T044** (US4): `/organizer/competitions/{id}/judges` →
`JudgeManagementComponent` → `POST .../judges` creates `Judge`+`Invitation(Pending)` rows and
returns immediately (`created`/`skipped`), then enqueues one `DispatchJobType.SendInvitation` job
per new judge — the first `DispatchJob` type with a real handler (`SendInvitationHandler`,
T041) since the queue/worker infrastructure (T016) landed. `DispatchWorker` (already running)
picks the job up, `SendInvitationHandler` calls `IKeycloakAdminClient` to provision the Keycloak
account + a fresh temporary password (never touching `Judge.KeycloakUserId`, which stays `null`
until the judge's real first login backfills it via `JudgeResolver`, T023) and `IEmailSender` to
deliver the invitation via Mailpit, then updates `Invitation.Status`/`SentAt`/`Attempts`/
`LastError` — the frontend's delivery-status table reflects this once it refreshes/reloads.
**T045–T049** (US5): `/organizer/competitions/{id}/tables` → `TableManagementComponent` loads
`GET /tables` + `GET /entries` + `GET /judges` in parallel, computes "Unassigned" client-side by
set difference, and renders one `MesaCard` per table. Every judge/beer assignment — drag-and-drop
or the keyboard "Move to" fallback — issues `PUT /tables/{id}` with that table's full desired
membership; `TableAssignmentApplier` validates COI over the complete submitted set before any
write (`409` + nothing persisted on conflict), diffs `TableJudge`/`TableSample` rows, and
flags/unflags `BeerEntry.NotValidForBos` competition-wide as table membership changes (FR-018) —
the response's `bosFlaggedEntryIds` drives the frontend's warning banner, and the component
refetches `GET /entries` wholesale afterward so every entry's flagged visual state (not just ones
touched by this one mutation) stays live without a reload. **T050–T054** (US6): `/judge/tables` →
`JudgeTablesListComponent` → `GET /me/tables` (own Judge rows resolved via `GetJudgeRecordsAsync`,
active `TableJudge` membership only, `Draft`-state competitions invisible) → selecting a table
opens `/judge/tables/:tableId` → `JudgeTableOrderComponent` → `GET /me/tables/{id}/samples` (blind
projection, `evaluationStatus` always `NotStarted` today since `Features/Evaluations` doesn't exist
until Phase 9) → local drag/keyboard reorder → `POST /me/tables/{id}/order` takes a row lock on the
`TastingTable`, assigns `SequenceOrder` 1..M, stamps `OrderFixedByJudgeId`/`At`, and — only once the
transaction has committed — emits `TableOrderFixed` to the `table:{tableId}` group; every other
judge with that table's sample view open and a live hub connection sees the fixed order and locked
state within FR-021's ≤1s budget with no refetch, `CompetitionHubService.joinTable` having already
subscribed them to that group on view-init. This is the first slice on either side of the codebase
that's judge-facing rather than organizer-facing, and the first REST-level consumer of
`ICurrentUser.GetJudgeRecordsAsync` (T023, previously exercised only by its own unit test) and of
`CompetitionHubService`'s realtime event stream (T020, previously wired but unconsumed). The
database holds the full domain schema (T008–T009); target contracts live in
`specs/001-birrapoint-mvp/contracts/` (REST `/api/v1`, SignalR `CompetitionHub`, `.xlsx` import
file). Frontend-side (ADR-0012, 2026-08-02): Keycloak's `check-sso` silently detects an existing
session at bootstrap without forcing a redirect, so an unauthenticated caller lands on the public
`/welcome` page rather than an immediate Keycloak-hosted login; signing in is now an explicit
action (`/auth/handoff` triggers the same PKCE redirect on demand). **T024**: once authenticated,
`''` resolves via `homeRedirectGuard` and `/organizer`/`/judge` via `organizerGuard`/`judgeGuard`,
all sharing
`role-landing.ts`'s single role→URL mapping, landing on `/organizer/dashboard` (still placeholder
— real dashboard data starts at Phase 11/US9) or `/judge/tables` (real content since Phase 8/US6,
above); a judge with a Keycloak `UPDATE_PASSWORD` required action never reaches any of this routing
at all, since Keycloak's hosted UI resolves it before the OIDC code exchange completes (no app code
involved, FR-003). Every outgoing `HttpClient` request to `apiBaseUrl` gets the access token
attached automatically (T019). T020's `CompetitionHubService` is consumed for the first time by
Phase 8/US6 (above); its `db.ts` (the Dexie offline store) still isn't — that's T060's job.
**T100–T101** (US13): `/organizer/dashboard` → `OrganizerDashboardComponent` → `GET /competitions`
(already existed, T027 — first frontend consumer) → each competition routes onward by its own
`state` field, no new backend round-trip needed. **T102–T103** (US13, FR-051): the same dashboard's
advance-state button → `POST /competitions/{id}/state` (already existed, T028) — now called from
real UI for the first time, not just tested directly — confirm → success refetches `GET
/competitions` (same call as page load, no separate "just this row" endpoint) → badge updates in
place.

**T055–T061** (US7): the offline-first judging loop, this MVP's core reason to exist. Once a
competition is `InEvaluation` and a table's order is fixed, `judge-table-order.component.ts` exposes
"Evaluate" only on the first `NotStarted` sample → `/judge/tables/:tableId/samples/:beerEntryId` →
`EvaluationSheetComponent` hydrates from any existing `SyncService.loadDraft()` result, then every
field change re-drafts (debounced ≤300ms, SC-003) regardless of connectivity. On submit,
`SyncService.submit()` writes the outbox row durably first — this is the one step that must
succeed before anything else, so the judge's work survives a reload even mid-flight — then, only if
online, attempts one immediate `POST /me/tables/{tableId}/evaluations`; a `201`/`200` clears the
outbox row and the draft, a definitive `400`/`409` surfaces to the judge, anything else (offline, a
flaky connection, a timeout) leaves the row queued and resolves the submit action anyway — the
offline-first guarantee is that "submit" is never blocked by the network. `replayOutbox()` (on the
`window` `online` event, service construction, and after every `submit()`) is what eventually
reconciles a queued row against the server with capped backoff. The backend's own idempotency
guarantee (a DB unique-constraint catch, not a pre-check) is what makes a replayed or raced
duplicate POST safe regardless of how many times the client's replay loop fires it.

**T062–T067** (US8, the last P1 story): once every active judge has submitted every sample at a
table with no open `DiscrepancyAlert`, the "Close Table" button in `judge-table-order.component.ts`
becomes enabled; confirming issues `POST /me/tables/{tableId}/close` → `CloseTableCommandHandler`
takes a `FOR UPDATE` row lock on the `TastingTable` inside an explicit transaction (senior-code-
reviewer finding on PR #23, mirroring `FixOrder.cs`'s identical one-shot-flip pattern — without it,
two judges racing the close endpoint could both pass the not-already-closed check and both commit,
double-emitting `TableClosed` to the organizer group), re-checks completeness
(`CloseTableRules.ComputeMissingBlindCodes`) and discrepancies server-side (never trusts the
client's gating alone), flips `TastingTable.State` to `Closed` and stamps `ClosedAt`, computes each
sample's consolidated mean (`CloseTableRules.ComputeMean`, rounded to 2 decimals — also a review
fix, the unrounded value was leaking repeating-decimal noise onto the wire), and only after that
transaction commits emits `TableClosed` twice with different payloads per audience — the
`table:{tableId}` group (judges) get only `{ tableId }`, the `competition:{id}:organizers` group also
gets the consolidated means (FR-042) for the monitoring dashboard Phase 11 will build. The closing
judge's own HTTP response is likewise minimal (`{ tableId }`, not the full `consolidatedScores`) —
another PR #23 review fix, since per-sample means are organizer-only data per
contracts/signalr-hub.md and the frontend never consumed the field it used to receive. From that
point, `SubmitEvaluation`'s pre-existing table-open check is what rejects any further submission to
that table (FR-034) — closing didn't need a new immutability guard, only to flip the flag that
check already reads. Separately, `PUT /competitions/{id}/evaluations/{evaluationId}`
(`CorrectEvaluationCommandHandler`, organizer-only, no UI yet — see Recorded debt) lets an organizer
revise a stored evaluation's scores/comments regardless of table state, re-validating the same caps
as `SubmitEvaluation`, writing an `EvaluationCorrected` audit row via `IAuditWriter` in the same
transaction as the correction (FR-035), and returning the recomputed total and consolidated mean so
a future organizer UI can update in place without a second round-trip.

**T068–T071** (US9): `/organizer/dashboard` → clicking an `InEvaluation`/`Finalized` competition →
`/organizer/competitions/{id}/monitor` → `CompetitionMonitorComponent` fetches `GET
/competitions/{id}/progress` (every table's completed/expected/percent), `GET /competitions/{id}`
(header), and `GET /competitions/{id}/entries` (for the drill-down sample list) in parallel, then
joins `competition:{id}:organizers` — the first real subscriber to that SignalR group in this
codebase, though `SubmitEvaluation.cs`'s `EvaluationCompleted` emit and `CloseTable.cs`'s
organizer-group `TableClosed` emit have both been firing into it, unconsumed, since Phase 9/10.
`FixOrder.cs` gained a matching organizer-group `TableOrderFixed` emit this phase specifically so
the dashboard would have all three event types tasks.md asked for. Selecting a blind code calls
`GET /competitions/{id}/entries/{entryId}/evaluations` for the read-only audit view (FR-038) — a
consolidated mean appears only once that entry's table has closed, computed the same way
`CloseTable.cs` computes it for the SignalR payload, just independently (see the Backend section
above for why this is a deliberate small duplication rather than a cross-slice import).

**T072–T078** (US10): the existing T102 advance-state button (`Finalized` transition) is FR-036's
actual trigger, no new UI — `ChangeState.cs` enqueues one `GeneratePdfs` `DispatchJob` after the
already-existing `tables-still-open` gate passes and the transaction commits. From there the
pipeline runs entirely in the background via the pre-existing `DispatchWorker` (T016): `GeneratePdfs
→ BundleZip → SendResultEmail` (one job per participant for the last stage), each stage enqueueing
the next on success, with progress visible via the already-generic `DispatchProgress` SignalR event
(no per-handler wiring needed — `DispatchWorker` already emits it for every job type). The organizer
reaches `/organizer/competitions/{id}/dispatch` via a new link on the monitor screen (shown only
once `Finalized`), which loads `GET .../dispatch` for per-participant status and joins the same
`competition:{id}:organizers` group `CompetitionMonitorComponent` already established a subscriber
pattern for, to receive live `DispatchProgress`. Downloading calls `GET .../results/archive`, which
either streams the persisted `ResultsArchive.ZipBytes` (`200`) or reports the `BundleZip` job's
current status (`202`) — the frontend already knows to fetch this as a `Blob` via `HttpClient`
rather than a plain link, since only `HttpClient` requests carry the auth token. A failed
participant email is retried via `POST .../dispatch/retries`, which resets that participant's
`SendResultEmail` job to a fresh `Pending` attempt, picked up by `DispatchWorker`'s existing
safety-net poll — no new retry mechanism, just reuse of what T016 already built.

## Recorded debt / immediate next steps

- **New (2026-08-02, senior-code-reviewer on PR #29)**: FR-019's `EntryInstructions` judge-
  visibility exception is conditioned on the organizer being able to review/edit/clear that text —
  but the wizard's import-step row summary (`import-step.component.ts`) doesn't show
  `entryInstructions` at all, and `onConsolidate` only gates on `unresolvedCount() > 0`, so a batch
  of all-`Valid` rows can consolidate without any row ever being opened for edit. Spec wording
  amended (Session 2026-08-02) to describe this as an available capability rather than a performed
  or enforced review, closing the immediate spec/code mismatch — but the underlying gap (nothing
  nudges the organizer to actually look at this field) is real and un-fixed. Two options on the
  table, neither picked yet: surface `entryInstructions` in the row summary so it's seen by
  default, or add a lightweight consolidation-time prompt when any row has non-empty
  `entryInstructions`.
- **Resolved 2026-07-29**: the `Organizer` table/`Competition.OrganizerId` FK
  (`Domain/Organizer.cs`, `OrganizerResolver`, `data-model.md`) now has its EF Core migration
  (`Migrations/20260729154727_AddOrganizers.cs`) — generated, applied against a real Postgres via
  Testcontainers, and verified: full `BirraPoint.Api.UnitTests` suite (189 tests) and
  `BirraPoint.Api.IntegrationTests` (including `OrganizerResolverTests` and every test that
  creates a `Competition`) pass.
- **Resolved 2026-07-22 (T102)**: `features/dashboard/organizer-dashboard.component.ts`'s
  feature→feature import of `CompetitionsApiService` (flagged by senior-code-reviewer on PR #21)
  is fixed — the service now lives in `core/api/`, both consuming features import it from there.
- **Resolved 2026-07-22 (T102–T103)**: the organizer dashboard now has a real advance-state action
  (FR-051) — `POST /competitions/{id}/state` (T028) is no longer only reachable via a raw API call;
  both E2E specs that used to work around its absence with a captured-bearer-token direct call now
  drive the real button. See `features/dashboard/` above for the implementation.
- **Resolved 2026-07-22 (T069)**: `TableOrderFixed` (T052) used to be emitted only to the
  `table:{tableId}` group even though contracts/signalr-hub.md documented an organizer-group row for
  it too — fixed by adding a matching `PublishToOrganizersAsync` call in `FixOrder.cs`; the live
  monitoring dashboard (T070) consumes it.
- **New (T070, US9), found by the E2E work, not by inspection**: the monitor dashboard's "Order
  fixed by {name}." note has no REST backfill — `GetProgressQuery`'s response carries no
  order-fixed field, so the note is populated *purely* from the live `TableOrderFixed` hub event.
  An organizer who opens `/organizer/competitions/{id}/monitor` *after* a table's order was already
  fixed will never see that note for that table, since fixing order is one-shot and no second event
  will ever arrive to populate it retroactively. Not a functional problem today — nothing depends on
  the note besides the organizer's own awareness, and `GetProgress`'s `state`/`percent` fields are
  unaffected — but worth a conscious fix (either add an `orderFixed`/`orderFixedByDisplayName` field
  to `TableProgressSummaryDto`, or accept the gap explicitly) before anything else comes to depend on
  this note being reliably present on first load.
- **Fixed 2026-07-22 (senior-code-reviewer, PR #23)**: three findings on `CloseTable.cs` fixed
  before merge — (1) no concurrency guard on the table-state flip, unlike the sibling `FixOrder.cs`
  one-shot flip; two judges racing `POST /close` could both pass the not-already-closed check and
  both commit, double-emitting `TableClosed` to the organizer group — fixed with the same `FOR
  UPDATE` row-lock-in-a-transaction pattern `FixOrder.cs` already uses; (2) the judge's own close
  response carried the full `consolidatedScores` payload, which `contracts/signalr-hub.md`
  deliberately withholds from judges (organizer-only) and which the frontend discarded anyway —
  fixed to return just `{ tableId }`; (3) `CloseTableRules.ComputeMean` returned unrounded
  full-precision `decimal` — fixed to round to 2 decimals before it goes over the wire. See
  `Features/Evaluations/CloseTable.cs`/`CloseTableRules.cs` and the Data flows section above for the
  post-fix behavior; `CloseTableApiTests.cs`'s happy-path test was reworked to assert the minimal
  response shape instead of the now-absent field.
- **Resolved 2026-07-28 (T089)**: every organizer/judge route is now covered by the axe-core sweep
  (`frontend/e2e/a11y/routes.a11y.spec.ts`) — closed with 3 real fixes, not just new coverage.
  (1) `WCAG 1.3.1` (`only-listitems`): `<ul cdkDropList>` in `mesa-card.component.ts` and
  `unassigned-column.component.ts` had `<app-judge-seat>`/`<app-beer-token>` custom elements as
  direct children instead of `<li>`. Fixed by changing those two leaf components' template root
  from `<li>` to `<div>` (identical attributes/directives/styles) plus `:host { display: contents }`
  so the host element stays invisible to layout, then wrapping each call site in a real `<li>`
  (the `mesa-seat` positioning class/inline-styles moved onto the new `<li>`). The `display:
  contents` addition wasn't cosmetic — without it, the host custom-element intercepted pointer
  events and broke real drag-and-drop, caught by re-running `us5-tables.spec.ts`, not by the a11y
  suite itself; 4 Jest specs asserting `HTMLLIElement` on these two components were updated to
  `HTMLDivElement` accordingly. (2) `WCAG 1.4.3` (contrast): `beer-token`'s white-on-`#d97706`
  (amber-600) was 3.18:1, below the 4.5:1 minimum — changed to `#92400e` (amber-800, ~7.09:1),
  including the matching focus-outline color. (3) `WCAG 1.4.1`/`1.4.11` (use-of-color / non-text
  contrast), found by senior-code-reviewer on PR #28, not by axe itself (it's not a text-contrast
  rule, and axe can't infer "this color difference is the only signal for this state"):
  `beer-token--bos-flagged`'s `NotValidForBos` state was conveyed by an inset ring color alone
  (`#dc2626` on the token's `#92400e` background, ~1.47:1, well under the 3:1 non-text minimum),
  with no text/icon alternative. Fixed with a visible `aria-hidden` marker glyph plus an
  `aria-describedby`-linked visually-hidden note ("Not valid for Best of Show"), and the ring
  recolored to `#fbbf24` (amber-400, ~4.24:1); the base `aria-label` ("Beer {code} — view details")
  deliberately stayed unchanged regardless of flag state, since several E2E specs locate a flagged
  beer by that exact accessible name. The old `smoke.spec.ts`/`home.a11y.spec.ts`
  `login-required`-race failures noted below are also resolved, by removing both specs entirely
  (see the E2E row in Testing & quality gates above for why).
- **Fixed 2026-07-22 (senior-code-reviewer, PR #22), narrows the item below**: idempotent replay
  didn't hold once a table closed or its competition moved past `InEvaluation` — `SubmitEvaluation`
  gated on table/competition state *before* checking for an already-persisted `(judge, entry)` row,
  so a judge whose evaluation had genuinely already committed (the ack was just lost — exactly the
  scenario the outbox replay engine exists for) got `409 table-closed` on retry instead of the
  stored `200`. Fixed by moving the existing-row check to the very top of the handler, before any
  precondition gate — a persisted evaluation is a fact regardless of what happens to the table
  afterward (FR-029/R-07). Regression test:
  `Replaying_an_already_stored_evaluation_after_the_table_closes_still_returns_200_not_409`.
  Same review pass also added a 15s RxJS `timeout()` to `sync.service.ts`'s submit/replay POST —
  `HttpClient` has no default one, so a hung socket on a nominally-online connection would have
  hung `submit()` and stuck `replayOutbox()`'s reentrancy guard indefinitely.
- **New (T060, US7), scope now narrower after the fix above**: an outbox row that hits a
  *definitive* rejection (`400`/`409`) still retries forever with capped backoff and never
  re-surfaces to the judge after the initial toast — but this can now only happen for a submission
  that never actually persisted in the first place and never legitimately will (e.g. the table
  closes or the competition moves past `InEvaluation` before the *first* attempt ever reaches the
  server) — not, as before the fix, for every already-successful submission whose ack merely got
  lost. Not a data-loss (the payload is intact, and SC-002's "exactly once" isn't violated, since it
  correctly never reaches the server), but still a user-visible dead end the judge has no way to
  discover or resolve short of clearing app storage. Fixing this needs real UI (surface stuck rows,
  let the judge retry/discard/investigate) — a small enough scope on its own that it's flagged here
  rather than folded into T060 itself.
- **New (T061, US7)**: a literal `page.reload()` while `context.setOffline(true)` is infeasible
  against this E2E suite's dev-mode harness — `npm start` (`ng serve`, what `playwright.config.ts`'s
  `webServer` runs) sends `no-cache` on every request and the PWA service worker is intentionally
  disabled outside production builds (`app.config.ts`: `enabled: !isDevMode()`), so a real network
  cut leaves Chromium with nothing to serve the document from. `us7-offline.spec.ts` substitutes
  `page.goBack()`/`page.goForward()` (pure client-side Angular Router navigation) as the closest
  in-harness equivalent that still destroys/reconstructs the component and exercises real Dexie
  rehydration — documented inline in the spec. A true cold-reload-while-offline proof would need a
  production build with the service worker enabled as part of the E2E harness — a bigger harness
  decision, tracked here for Phase 15 rather than solved ad hoc in this task.
- **New (T065, US8)**: `PUT /competitions/{id}/evaluations/{evaluationId}` (`CorrectEvaluation.cs`)
  is implemented, contract-tested (5 integration cases: success + audit, non-owning-organizer 404,
  non-existent-evaluation 404, out-of-range score 400, short comment 400), but has no organizer-facing
  UI yet — there is currently no screen where an organizer would discover a discrepancy-resolved or
  otherwise-wrong evaluation and trigger a correction. FR-035 is satisfied at the API/audit layer;
  the UI is Phase 11 (US9, Live Monitoring Dashboard) territory, the natural place an organizer would
  first see a score worth correcting.
- **Observed, not a regression**: `Two_near_simultaneous_submissions_for_the_same_pair_leave_
  exactly_one_row` (`SubmitEvaluationApiTests.cs`, a `Task.WhenAll`-driven race test from T056)
  failed once in a full-suite run (`Expected: 1, Actual: 0`) during this phase's verification, but
  passed in isolation and on a full-suite re-run immediately after. Concluded pre-existing timing
  sensitivity in a genuine-concurrency test (not something T062-T067 touched), not chased further —
  flagged here in case it recurs often enough to be worth a more deterministic race-inducing
  technique (e.g. a `SemaphoreSlim` barrier instead of bare `Task.WhenAll`).
- **New, security-relevant**: real judge invitations never grant the Keycloak `JUDGE` realm role.
  `RegisterJudgesCommandHandler` → `SendInvitationHandler` → `IKeycloakAdminClient.
  EnsureUserWithTemporaryPasswordAsync` (`Common/Keycloak/KeycloakAdminClient.cs`) creates/updates
  the Keycloak user but never calls a role-mappings endpoint, while the `JUDGE` authorization policy
  is a plain `RequireRole("JUDGE")` (`Common/Auth/AuthenticationExtensions.cs`). Net effect: a judge
  invited purely through the organizer's "Register judges" UI — no test-harness shortcut involved —
  completes the forced password change and then gets `403` on every judge-facing endpoint forever;
  the product's real invitation path cannot currently produce a working judge account.
  `frontend/e2e/us4-judges.spec.ts` doesn't catch this because it only asserts the invitation email
  lands in Mailpit, never logs the invited judge in. Found while building T054's E2E spec (which
  needed two real judge logins) — `frontend/e2e/support/keycloak-admin.ts`'s test-only
  `createJudgeUser` already does the correct role-assignment call, so the spec provisions its own
  judges through that instead of the real invitation path and isn't blocked by the bug, but the bug
  itself is unfixed. **Real fix**: add a `POST /admin/realms/{realm}/users/{id}/role-mappings/realm`
  call to `KeycloakAdminClient.CreateUserAsync` (mirroring `keycloak-admin.ts`'s test helper) plus
  an integration/E2E assertion that an invited-only judge can actually log in and reach a
  `JUDGE`-authorized endpoint — deliberately not patched inline with T050–T054 since it's a
  different story's slice (T038–T044) and warrants its own deliberate fix + tests.
- **Resolved 2026-07-22 (T069/US9)** — was: `TableOrderFixed` (T052) emitted only to the
  `table:{tableId}` group though contracts/signalr-hub.md also listed it under the organizer
  group's event table. Closed together with US9 exactly as this entry anticipated — see the
  "Resolved 2026-07-22 (T069)" entry above.
- **New**: `angular.json`'s CLI bundle-size budget (`maximumWarning: 500kB` raw) now trips a build
  warning as of T053 (~547.1 kB raw) — the actual constitutional gate (Principle IX, ≤500 kB
  **gzip**) is still comfortably met at ~136.5 kB transfer, so this isn't a Definition-of-Done
  blocker, but the margin to the CLI's own warning threshold is gone; worth revisiting the
  `angular.json` budget numbers (or trimming what's eagerly bundled) before the next feature adds
  more to the initial chunk.
- **Updated**: `UpdateJudgeEmail` (T042, `Features/Judges/UpdateJudgeEmail.cs`) still does not
  implement the COI-matching/BOS-reflagging re-run against the new address that
  `contracts/rest-api.md` cites (FR-017/FR-018). The original blocker (`Features/Tables` not
  existing) is gone — Phase 7 built it, and `CoiDetector`/`BosFlagRules` (`Features/Tables/`) are
  exactly the pure helpers this would reuse — but wiring `UpdateJudgeEmail` to them was never
  actually in either phase's task list, so it's still unbuilt. Low urgency in practice (this
  endpoint only matters pre-first-login, before a judge could plausibly be assigned to a table
  too), but a real gap between the contract's stated behavior and the code — pick up as a small
  follow-up task rather than silently continuing to defer it indefinitely.
- **New**: T048A's beer/judge detail modals ship without allergen/special-award beer badges or
  judge BJCP-category/certification fields. These were referenced only in a prior session's
  tasks.md edit (commit 9ec60a4, from an organizer-supplied UI prototype) with zero backing in
  spec.md/data-model.md/contracts/any import column — the process this repo requires (flow
  requirement changes back into the spec first) wasn't followed at the time. User-approved
  decision this phase: scope them out rather than invent the data model/UI for unspecified
  product behavior. If these are wanted, they need a proper spec amendment first (where does the
  data come from — an import column? organizer-entered post-consolidation? judge self-reported at
  registration?) before any code should reference them again.
- **New**: `frontend/src/app/features/table-management/click-vs-drag.directive.ts`'s actual CDK
  pointer/drag gesture is not exercised by Jest — jsdom can't simulate real pointer
  capture/movement reliably. Verified manually in a real browser during T048 (screenshot +
  `boundingBox()` measurements) and covered by T049's E2E (real `mouse.down`/multi-step
  `mouse.move`/`mouse.up` sequences against CDK's drop lists) instead. Same "browser E2E fills the
  jsdom gap" pattern as the FormsModule bug found in Phase 5 — worth remembering that any future
  pointer-gesture-dependent UI in this codebase needs the same treatment, Jest alone won't catch
  a regression there.
- **New**: `SendInvitationHandler` (T041) requires a `Frontend:BaseUrl` config value (used to build
  the invitation email's login link) that's only ever set by `AppHost.cs`'s Aspire env injection —
  there's no fallback in `appsettings.json`/`appsettings.Development.json`. Harmless today (every
  local/test path goes through Aspire or a test-config override), but will need a real value wired
  into whatever non-Aspire deployment Phase 16 (`azd`) produces, or the job will fail every time in
  that environment. Flagged during T039 review; not fixed here since Phase 16 doesn't exist yet.
- **Resolved 2026-07-28 (T089)**: `frontend/e2e/smoke.spec.ts` and `frontend/e2e/a11y/
  home.a11y.spec.ts` (both dating to T004, before Keycloak's `login-required` existed) — removed
  rather than fixed. `us1-auth.spec.ts` already asserts the Keycloak redirect `smoke.spec.ts` was
  crudely checking, and the new comprehensive `routes.a11y.spec.ts` supersedes scanning `/` (which
  has never rendered app content since `login-required` landed — nothing left to scan there).
- **T094 (2026-07-28), audit only, no findings**: security pass over deny-by-default authorization
  (every endpoint's role check cross-referenced against `contracts/rest-api.md`'s role matrix,
  including `CompetitionHub`'s independent DB-backed ownership/membership guards beyond the
  role-claim check), secrets handling (only the already-documented FR-046 local-dev Keycloak
  placeholders found, correctly scoped), `AuditLog`/logger call sites (no credentials, tokens, or
  entrant-identity leakage — moot for judges regardless, since no endpoint exposes `AuditLog` to a
  `JUDGE`-authorized route), and EF Core query construction (`FromSqlInterpolated` only, zero
  `FromSqlRaw`/string-concatenated SQL anywhere). All four checks passed with zero code changes.
- **New (2026-07-28, found during T091 validation, not caused by Phase 15 work)**: `frontend/src/
  styles.css` has unrelated, uncommitted, broken design-token CSS (`Cannot apply unknown utility
  class 'border-1.5'`) that fails `ng build`/Tailwind compilation outright — first noticed mid-PR
  #27 as inert dirty-tree noise, now an active blocker for anyone who builds this branch. Currently
  parked in a **local `git stash`** (not discarded) on whichever machine ran Phase 15's validation —
  a machine-local fix, invisible to any other clone or CI runner, not a substitute for actually
  fixing or reverting the file. Still unresolved: whoever picks this up needs to either fix the
  `border-1.5` utility class (or whatever it was meant to be) or drop the change outright, and that
  fix needs to actually land in a commit, not stay stashed on one machine.
- **ADR-0003**: decide zoneless change detection before further frontend work.
- **ADR-0004**: domain state/status enums are stored as strings in PostgreSQL (T009).
- **ADR-0006**: `CompetitionHub`'s DB-backed join-authorization (ownership/membership checks) still
  has no integration/contract test. T021's `AuthPolicyTests` proves the same `"ORGANIZER"` +
  ownership-scoping *pattern* over a diagnostic stand-in (`/__test/` endpoints), which is not the
  same as testing the hub itself — the `WebApplicationFactory` harness it needs now exists (T018),
  a real SignalR test client (`HubConnectionBuilder` against the test server) is the natural next
  step, but no task has written that coverage yet.
- Same gap, different task: `DispatchJobQueue`/`DispatchWorker`'s DB-backed enqueue and
  resume/dispatch loop (T016) have no integration test yet either — same T018 harness, still
  unwritten.
- Production/`azd` deployment (T096) must set `Keycloak__ApiAudience` consistently with whatever
  audience value the production realm's mapper stamps (ADR-0009).
- `WaitFor` a *ready* Keycloak once auth is wired (T011; ADR-0001 mitigation).
- `Aspire.Hosting.NodeJs` is on the old version train (9.5.2); align when a 13.x ships.
- Add a webkit Playwright project before writing the offline E2E suites (iOS Safari is the
  constrained target for the offline engine, R-08).
- No `.gitattributes` in the repo: Prettier `endOfLine: "auto"` (T007) keeps `format:check`
  green on both CRLF (Windows autocrlf) and LF checkouts, but a contributor with
  `core.autocrlf=false` on Windows could still commit CRLF blobs unnoticed. Durable fix:
  `* text=auto eol=lf` + Prettier `endOfLine: "lf"` as its own follow-up task (PR #3 review).
- `/health`//`/alive` exposure strategy for ACA probes (Phase 16).
