---

description: "Task list for BirraPoint MVP — Beer Competition Blind-Tasting Platform"
---

# Tasks: BirraPoint MVP — Beer Competition Blind-Tasting Platform

**Input**: Design documents from `/specs/001-birrapoint-mvp/`

**Prerequisites**: plan.md, spec.md, data-model.md, contracts/ (rest-api, signalr-hub, import-file), research.md, quickstart.md

**Tests**: MANDATORY (Constitution Principle III). Every user story includes test tasks, written first and failing before implementation. The quickstart scenario numbers referenced below map 1:1 to user stories.

**Organization**: Tasks are grouped by user story (US1–US12, priority order P1 → P3) so each story is an independently implementable, testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Include exact file paths in descriptions

## Path Conventions

Per plan.md §Project Structure: backend at `backend/src/BirraPoint.Api/` (slices under `Features/`,
shared kernel `Domain/` + `Common/`, hub in `Realtime/`), tests at `backend/tests/`, frontend at
`frontend/src/app/` (FSD: `core/`, `features/`, `shared/`), E2E at `frontend/e2e/`, infra at `infra/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Repos, toolchains, and local environment (.NET 10 / Angular 20 / Aspire per constitution v1.2.0)

- [X] T001 Create backend solution: `backend/BirraPoint.sln`, project `backend/src/BirraPoint.Api/BirraPoint.Api.csproj` (net10.0) with folder skeleton `Domain/`, `Common/`, `Features/`, `Realtime/`, and packages MediatR 12.5.x, FluentValidation, EF Core + Npgsql, ClosedXML, QuestPDF, MailKit
- [X] T002 [P] Create test projects `backend/tests/BirraPoint.Api.UnitTests/` and `backend/tests/BirraPoint.Api.IntegrationTests/` (xUnit; integration adds Testcontainers.PostgreSql + Microsoft.AspNetCore.Mvc.Testing), wired into `backend/BirraPoint.sln`
- [X] T003 [P] Scaffold Angular 20 standalone workspace in `frontend/` with `@angular/pwa`, Tailwind CSS, `@angular/cdk`, `keycloak-angular`/`keycloak-js`, `dexie`, `@microsoft/signalr` in `frontend/package.json`; FSD skeleton `src/app/core|features|shared`
- [X] T004 [P] Configure Jest via jest-preset-angular in `frontend/jest.config.ts` and Playwright + `@axe-core/playwright` in `frontend/e2e/playwright.config.ts`
- [X] T005 [P] Create Aspire orchestration: `backend/src/BirraPoint.AppHost/` (PostgreSQL 16, Keycloak with realm import, Mailpit, the API project, frontend as npm resource) and `backend/src/BirraPoint.ServiceDefaults/` (OpenTelemetry, health checks, resilience), both wired into `backend/BirraPoint.sln`; author `infra/keycloak/birrapoint-realm.json` (realm `birrapoint`, roles `ORGANIZER`/`JUDGE`, seeded organizer user, backend admin service-account client with `manage-users`, SPA public client with PKCE)
- [X] T006 [P] Configure linting/formatting: `backend/.editorconfig` (dotnet format clean) and `frontend/eslint.config.js` + Prettier
- [ ] T007 Update `CLAUDE.md` Project Status section with the real build/run/test commands from quickstart.md (Constitution Principle X)

**Checkpoint**: `dotnet run --project backend/src/BirraPoint.AppHost` starts the full local topology (FR-044); empty API and blank Angular app build and run

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Persistence, auth, pipeline, error model, realtime skeleton — nothing story-specific

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T008 Create shared-kernel entities and enums from data-model.md in `backend/src/BirraPoint.Api/Domain/` (Competition, BjcpStyle, Participant, BeerEntry, EntryCollaborator, Judge, Invitation, TastingTable, TableJudge, TableSample, Evaluation, DiscrepancyAlert, DispatchJob, AuditLog + state enums)
- [ ] T009 Create `AppDbContext` + entity configurations (unique indexes incl. `(JudgeId, BeerEntryId)`, computed `Evaluation.Total` column, `EndDate >= StartDate` check, partial unique index for open DiscrepancyAlert) in `backend/src/BirraPoint.Api/Common/Persistence/`, generate initial EF migration
- [ ] T010 [P] Add BJCP 2021 style catalog JSON at `backend/src/BirraPoint.Api/Features/Catalog/Data/bjcp-2021.json` and seed it via EF migration (R-12)
- [ ] T011 JWT bearer auth against Keycloak, deny-by-default fallback policy, `ORGANIZER`/`JUDGE` role policies, and `CurrentUser` claims accessor (sub/email/roles) in `backend/src/BirraPoint.Api/Common/Auth/`
- [ ] T012 [P] ProblemDetails middleware + exception mapping with the 14 stable `urn:birrapoint:*` type URNs from contracts/rest-api.md §Error catalog in `backend/src/BirraPoint.Api/Common/Errors/`
- [ ] T013 [P] MediatR registration + FluentValidation `ValidationBehavior` pipeline in `backend/src/BirraPoint.Api/Common/Behaviors/`
- [ ] T014 [P] `AuditWriter` service (action, entity, actor, before/after jsonb) in `backend/src/BirraPoint.Api/Common/Audit/`
- [ ] T015 `CompetitionHub` with guarded group joins (`JoinCompetitionAsOrganizer` → role+ownership, `JoinTable` → active membership) per contracts/signalr-hub.md in `backend/src/BirraPoint.Api/Realtime/CompetitionHub.cs`; emit-after-commit event dispatcher in `Realtime/EventPublisher.cs`
- [ ] T016 `DispatchJob` queue service + hosted `DispatchWorker` (Channels wake-up, startup resume, retry/backoff per R-06) in `backend/src/BirraPoint.Api/Common/Jobs/`
- [ ] T017 First slice proving the pipeline: `GET /api/v1/styles` in `backend/src/BirraPoint.Api/Features/Catalog/GetStyles.cs`
- [ ] T018 Integration-test harness: `WebApplicationFactory` + Testcontainers PostgreSQL + configurable test JWT issuer in `backend/tests/BirraPoint.Api.IntegrationTests/TestHost/`; first contract test `Catalog/GetStylesTests.cs` (auth required, payload shape)
- [ ] T019 [P] Frontend core auth: Keycloak initialization (PKCE), token interceptor, role guards, route shells `/organizer/**` and `/judge/**` in `frontend/src/app/core/auth/`
- [ ] T020 [P] Frontend core services: typed API client with ProblemDetails→UI error mapping in `frontend/src/app/core/api/`, SignalR connection service with rejoin-on-reconnect in `frontend/src/app/core/realtime/`, Dexie database (`drafts`, `outbox` stores) in `frontend/src/app/core/offline/db.ts`

**Checkpoint**: Foundation ready — authenticated `GET /styles` round-trip works from the Angular shell; user story implementation can now begin

---

## Phase 3: User Story 1 - Secure Access with Role-Based Entry (Priority: P1) 🎯 MVP

**Goal**: Keycloak login, forced password change for invited judges, role-based landing

**Independent Test**: quickstart.md scenario 1

### Tests for User Story 1 (MANDATORY — write first, must fail) ⚠️

- [ ] T021 [P] [US1] Contract tests: protected endpoint without token → 401; JUDGE token on ORGANIZER endpoint → 403; scoping returns 404 outside caller's data in `backend/tests/BirraPoint.Api.IntegrationTests/Auth/AuthPolicyTests.cs`
- [ ] T022 [P] [US1] E2E: unauthenticated visit redirects to Keycloak; organizer lands on `/organizer/dashboard`; judge lands on `/judge/tables`; temp-credential judge is forced through password change with no deep-link bypass in `frontend/e2e/us1-auth.spec.ts`

### Implementation for User Story 1

- [ ] T023 [US1] Judge identity resolution: match authenticated email→`Judge` rows, persist `KeycloakUserId`/`DisplayName` on first authenticated call, expose helper on `CurrentUser` in `backend/src/BirraPoint.Api/Common/Auth/JudgeResolver.cs`
- [ ] T024 [US1] Frontend post-login routing by realm role + landing placeholders (organizer dashboard shell, judge tables shell) in `frontend/src/app/features/auth/`

**Checkpoint**: Scenario 1 passes end to end

---

## Phase 4: User Story 2 - Competition Creation Wizard with Drafts (Priority: P1)

**Goal**: Multi-step wizard, per-step validation, Draft save/resume, lifecycle transitions

**Independent Test**: quickstart.md scenario 2

### Tests for User Story 2 (MANDATORY — write first, must fail) ⚠️

- [ ] T025 [P] [US2] Unit tests: CreateCompetition/UpdateCompetition validators (required fields, endDate ≥ startDate) and ChangeState gates (forward-only, skip-free, edits rejected in `InEvaluation`) in `backend/tests/BirraPoint.Api.UnitTests/Competitions/`
- [ ] T026 [P] [US2] Contract tests: POST/GET/PUT `/competitions`, `POST /competitions/{id}/state` happy + `409 invalid-state-transition` cases in `backend/tests/BirraPoint.Api.IntegrationTests/Competitions/CompetitionsApiTests.cs`

### Implementation for User Story 2

- [ ] T027 [US2] Slices CreateCompetition, UpdateCompetition, GetCompetition, ListCompetitions + endpoint mapping in `backend/src/BirraPoint.Api/Features/Competitions/`
- [ ] T028 [US2] Slice ChangeCompetitionState with FR-006 state gates, audit entry, `CompetitionStateChanged` emit in `backend/src/BirraPoint.Api/Features/Competitions/ChangeState.cs`
- [ ] T029 [US2] Frontend wizard: stepper with per-step required-field validation ("Next" disabled), Save Draft, resume-with-data in `frontend/src/app/features/competition-wizard/`
- [ ] T030 [US2] E2E scenario 2 in `frontend/e2e/us2-wizard.spec.ts`

**Checkpoint**: Scenarios 1–2 pass independently

---

## Phase 5: User Story 3 - Beer Entry Import with In-Flow Correction (Priority: P1)

**Goal**: `.xlsx` upload, row validation vs BJCP catalog, Mapping & Correction, consolidation with blind codes

**Independent Test**: quickstart.md scenario 3

### Tests for User Story 3 (MANDATORY — write first, must fail) ⚠️

- [ ] T031 [P] [US3] Unit tests: workbook parsing per contracts/import-file.md (header matching, row statuses, duplicate pair, collaborators split), style matching by code/exact name, blind-code generation uniqueness in `backend/tests/BirraPoint.Api.UnitTests/Import/`
- [ ] T032 [P] [US3] Contract tests: upload → row results; resolve row (assign-style/exclude); consolidate blocked with `409 unresolved-import-rows`; success returns blind codes; `400 invalid-import-file` for malformed uploads in `backend/tests/BirraPoint.Api.IntegrationTests/Import/ImportApiTests.cs`

### Implementation for User Story 3

- [ ] T033 [US3] Slice-owned staging entities `ImportBatch`/`ImportRow` + EF migration (single active batch per competition) in `backend/src/BirraPoint.Api/Features/Import/ImportBatch.cs`; document them in `specs/001-birrapoint-mvp/data-model.md` (Principle X)
- [ ] T034 [US3] ClosedXML workbook parser implementing contracts/import-file.md in `backend/src/BirraPoint.Api/Features/Import/WorkbookParser.cs`
- [ ] T035 [US3] Slices UploadImport, GetImport, ResolveRow, ConsolidateImport (creates Participants/BeerEntries/Collaborators + blind codes, FR-013) + endpoints in `backend/src/BirraPoint.Api/Features/Import/`
- [ ] T036 [US3] Frontend import feature: upload, per-row results, Mapping & Correction screen with searchable catalog picker + exclude, consolidate summary in `frontend/src/app/features/entry-import/`
- [ ] T037 [US3] E2E scenario 3 + fixture `frontend/e2e/fixtures/entries-with-errors.xlsx` (mirrors the contract example incl. `99Z` row) in `frontend/e2e/us3-import.spec.ts`

**Checkpoint**: Import flow complete with correction loop

---

## Phase 6: User Story 4 - Judge Registration and Automatic Invitations (Priority: P1)

**Goal**: Bulk email registration, Keycloak provisioning, tracked invitation emails

**Independent Test**: quickstart.md scenario 4

### Tests for User Story 4 (MANDATORY — write first, must fail) ⚠️

- [ ] T038 [P] [US4] Unit tests: bulk registration dedup (in-list + already-registered with reasons), email validation in `backend/tests/BirraPoint.Api.UnitTests/Judges/`
- [ ] T039 [P] [US4] Contract tests: `POST /competitions/{id}/judges` created/skipped semantics, `GET` delivery statuses, resend invitation, email correction (`PUT .../judges/{judgeId}` incl. `409 judge-already-active` and COI/BOS re-run) in `backend/tests/BirraPoint.Api.IntegrationTests/Judges/JudgesApiTests.cs`

### Implementation for User Story 4

- [ ] T040 [US4] Keycloak Admin API client (create user, temp password, `UPDATE_PASSWORD` required action, idempotent on existing user) in `backend/src/BirraPoint.Api/Common/Keycloak/KeycloakAdminClient.cs`
- [ ] T041 [US4] MailKit SMTP sender + invitation template; `SendInvitation` DispatchJob handler updating `Invitation` status/attempts/lastError in `backend/src/BirraPoint.Api/Common/Email/` and `Features/Judges/SendInvitationHandler.cs`
- [ ] T042 [US4] Slices RegisterJudges (bulk), GetJudges, ResendInvitation, UpdateJudgeEmail (pre-first-login correction: uniqueness, COI/BOS re-run, Keycloak update) + endpoints in `backend/src/BirraPoint.Api/Features/Judges/`
- [ ] T043 [US4] Frontend judge-management: paste list, created/skipped report, delivery status, edit email (with resend) in `frontend/src/app/features/judge-management/`
- [ ] T044 [US4] E2E scenario 4 (assert invitation visible in Mailpit API) in `frontend/e2e/us4-judges.spec.ts`

**Checkpoint**: Judges provisioned end to end; forced password change from US1 applies to them

---

## Phase 7: User Story 5 - Table Setup with Conflict-of-Interest Protection (Priority: P1)

**Goal**: Tables with judges+samples; COI hard block; "Not valid for BOS" flagging

**Independent Test**: quickstart.md scenario 5

### Tests for User Story 5 (MANDATORY — write first, must fail) ⚠️

- [ ] T045 [P] [US5] Unit tests: COI detection via owner/collaborator email match; BOS flag on any table membership; flag lift only before first submitted evaluation (FR-018 permanence) in `backend/tests/BirraPoint.Api.UnitTests/Tables/`
- [ ] T046 [P] [US5] Contract tests: `409 conflict-of-interest` with `{judgeId, beerEntryIds}` payload and atomic rollback; success returns `bosFlaggedEntryIds`; entry-in-one-table uniqueness in `backend/tests/BirraPoint.Api.IntegrationTests/Tables/TablesApiTests.cs`

### Implementation for User Story 5

- [ ] T047 [US5] Slices CreateTable, UpdateTable, ListTables with transactional COI validation + BOS flagging/unflagging + endpoints in `backend/src/BirraPoint.Api/Features/Tables/`
- [ ] T048 [US5] Frontend table-management: table builder (judges + samples pickers), COI conflict dialog, BOS warning banner in `frontend/src/app/features/table-management/`
- [ ] T049 [US5] E2E scenario 5 in `frontend/e2e/us5-tables.spec.ts`

**Checkpoint**: All organizer provisioning (US2–US5) complete

---

## Phase 8: User Story 6 - Blind Table Dynamics: Shared Fixed Order (Priority: P1)

**Goal**: Blind sample list, drag & drop + keyboard reorder, one-shot order fix propagated ≤1 s, sequential gating

**Independent Test**: quickstart.md scenario 6

### Tests for User Story 6 (MANDATORY — write first, must fail) ⚠️

- [ ] T050 [P] [US6] Unit tests: FixOrder permutation validation, one-shot semantics (`order-already-fixed`), state gating (Active/InEvaluation only) in `backend/tests/BirraPoint.Api.UnitTests/TastingOrder/`
- [ ] T051 [P] [US6] Contract tests: `/me/tables`, `/me/tables/{id}/samples` (**assert serialized payload contains no `beerName`/participant fields — BR-01 structural test**), order fix race → exactly one winner in `backend/tests/BirraPoint.Api.IntegrationTests/TastingOrder/OrderApiTests.cs`

### Implementation for User Story 6

- [ ] T052 [US6] Slices GetMyTables, GetTableSamples (JudgeSampleDto projection per data-model.md §Anonymity), FixOrder (+ `TableOrderFixed` emit) + endpoints in `backend/src/BirraPoint.Api/Features/TastingOrder/`
- [ ] T053 [US6] Frontend judge-tables: blind list, CDK drag-drop **plus keyboard reorder alternative (move up/down buttons, FR-020)**, Fix Order confirm, "Order fixed by Judge X" locked state, live reorder on hub event in `frontend/src/app/features/judge-tables/`
- [ ] T054 [US6] E2E scenario 6 with two judge sessions (propagation ≤1 s, lock, sequence gate) in `frontend/e2e/us6-order.spec.ts`

**Checkpoint**: Blind ordered judging flow ready for evaluation capture

---

## Phase 9: User Story 7 - Offline-First Validated Evaluation Sheet (Priority: P1)

**Goal**: Capped sectioned sheet, auto-total, ≥20-char comments, ≤300 ms local drafts, exactly-once sync

**Independent Test**: quickstart.md scenario 7

### Tests for User Story 7 (MANDATORY — write first, must fail) ⚠️

- [ ] T055 [P] [US7] Unit tests: SubmitEvaluation validator (caps 12/3/20/5/10, comments ≥20), preconditions (state, order fixed, next-in-sequence), locked-on-submit rule in `backend/tests/BirraPoint.Api.UnitTests/Evaluations/SubmitEvaluationTests.cs`
- [ ] T056 [P] [US7] Contract tests: `201` submit, idempotent replay `200` with stored result (`X-Idempotency-Key`), `409` order-not-fixed / out-of-sequence / invalid-state / evaluation-locked in `backend/tests/BirraPoint.Api.IntegrationTests/Evaluations/SubmitEvaluationApiTests.cs`
- [ ] T057 [P] [US7] Jest unit tests: draft store writes ≤300 ms debounce contract, outbox replay (online event, app start, backoff, dedupe on replay-200) in `frontend/src/app/core/offline/sync.service.spec.ts`

### Implementation for User Story 7

- [ ] T058 [US7] Slice SubmitEvaluation: preconditions, idempotent create, `EvaluationCompleted` emit (organizer group), discrepancy detection hook point (activated in US11) in `backend/src/BirraPoint.Api/Features/Evaluations/SubmitEvaluation.cs`
- [ ] T059 [US7] Frontend evaluation sheet: five capped numeric sections + comment fields with live remaining-length, auto-computed read-only total, submit gating in `frontend/src/app/features/evaluation-sheet/`
- [ ] T060 [US7] Frontend offline engine: draft persistence (≤300 ms after change), offline badge ("Offline mode — data protected locally"), outbox enqueue on submit, `SyncService` replay per R-08, and an explicit warning when local storage is unavailable/full (spec edge case — never fail silently) in `frontend/src/app/core/offline/sync.service.ts`
- [ ] T061 [US7] E2E scenario 7: fill offline (Playwright `setOffline`), reload → draft intact, reconnect → exactly one server evaluation in `frontend/e2e/us7-offline.spec.ts`

**Checkpoint**: 🎯 Core judging loop (US1+US6+US7) demonstrable end to end

---

## Phase 10: User Story 8 - Table Closing and Score Immutability (Priority: P1)

**Goal**: Close gate (complete + no open discrepancy), permanent judge read-only, audited organizer corrections

**Independent Test**: quickstart.md scenario 8

### Tests for User Story 8 (MANDATORY — write first, must fail) ⚠️

- [ ] T062 [P] [US8] Unit tests: close preconditions (missing list), immutability guard incl. late offline sync, consolidated mean computation (FR-042) in `backend/tests/BirraPoint.Api.UnitTests/Evaluations/CloseTableTests.cs`
- [ ] T063 [P] [US8] Contract tests: `409 evaluations-incomplete`, post-close judge mutations → `409 table-closed`, organizer `PUT /competitions/{id}/evaluations/{evaluationId}` succeeds + AuditLog row in `backend/tests/BirraPoint.Api.IntegrationTests/Evaluations/CloseTableApiTests.cs`

### Implementation for User Story 8

- [ ] T064 [US8] Slices CloseTable (gate checks, consolidated means, `TableClosed` emit) and immutability enforcement across all evaluation mutations in `backend/src/BirraPoint.Api/Features/Evaluations/CloseTable.cs`
- [ ] T065 [US8] Slice CorrectEvaluation (organizer-only, re-validates caps/lengths, recomputes total + mean, audit before/after) per contracts/rest-api.md in `backend/src/BirraPoint.Api/Features/Evaluations/CorrectEvaluation.cs`
- [ ] T066 [US8] Frontend: Close Table flow with missing-evaluations dialog; read-only sheet rendering after close in `frontend/src/app/features/judge-tables/close-table/`
- [ ] T067 [US8] E2E scenario 8 in `frontend/e2e/us8-close.spec.ts`

**Checkpoint**: All P1 stories complete — full judging lifecycle works

---

## Phase 11: User Story 9 - Live Monitoring Dashboard with Audit (Priority: P2)

**Goal**: Real-time per-table progress (≤1 s, no reload), read-only audit drill-down

**Independent Test**: quickstart.md scenario 9

### Tests for User Story 9 (MANDATORY — write first, must fail) ⚠️

- [ ] T068 [P] [US9] Contract tests: `GET /competitions/{id}/progress` shape, `GET .../entries/{entryId}/evaluations` audit payload (judge names + mean when closed), organizer-only access in `backend/tests/BirraPoint.Api.IntegrationTests/Monitoring/MonitoringApiTests.cs`

### Implementation for User Story 9

- [ ] T069 [US9] Slices GetProgress, GetEntryEvaluations (audit read model) + endpoints in `backend/src/BirraPoint.Api/Features/Monitoring/`
- [ ] T070 [US9] Frontend dashboard: initial load from `/progress`, live updates via `EvaluationCompleted`/`TableClosed`/`TableOrderFixed` (smooth update, no reload/flicker), audit drill-down read-only view, per-table state badges in `frontend/src/app/features/dashboard/`
- [ ] T071 [US9] E2E scenario 9 incl. ≤1 s latency assertion in `frontend/e2e/us9-dashboard.spec.ts`

**Checkpoint**: Organizer can run a live event visually

---

## Phase 12: User Story 10 - Event Closing with Automated Results Dispatch (Priority: P2)

**Goal**: Finalize gate, background PDFs, structured ZIP, per-participant emails with retry

**Independent Test**: quickstart.md scenario 10

### Tests for User Story 10 (MANDATORY — write first, must fail) ⚠️

- [ ] T072 [P] [US10] Unit tests: finalize precondition (`tables-still-open`), ZIP path scheme `/Competition/Participant/Style_BlindCode.pdf`, email job retry/failure accounting in `backend/tests/BirraPoint.Api.UnitTests/Dispatch/`
- [ ] T073 [P] [US10] Contract tests: finalize → jobs enqueued; archive `202` then `200` ZIP with correct hierarchy; dispatch status list; retry endpoint re-queues failures in `backend/tests/BirraPoint.Api.IntegrationTests/Dispatch/DispatchApiTests.cs`

### Implementation for User Story 10

- [ ] T074 [US10] QuestPDF score-sheet document (blind code, style, sections, comments, total, consolidated mean, judge display name per R-14) in `backend/src/BirraPoint.Api/Features/Dispatch/ScoreSheetDocument.cs`
- [ ] T075 [US10] DispatchJob handlers GeneratePdfs → BundleZip → SendResultEmail (per participant, status/attempts/lastError) + `DispatchProgress` emits; finalize transition enqueues pipeline in `backend/src/BirraPoint.Api/Features/Dispatch/`
- [ ] T076 [US10] Endpoints: results archive stream (`202`/`200`), dispatch status, retries in `backend/src/BirraPoint.Api/Features/Dispatch/Endpoints.cs`
- [ ] T077 [US10] Frontend results-dispatch: finalize action, generation progress, ZIP download, per-recipient statuses + retry in `frontend/src/app/features/results-dispatch/`
- [ ] T078 [US10] E2E scenario 10 (Mailpit attachment assertions, ZIP structure) in `frontend/e2e/us10-dispatch.spec.ts`

**Checkpoint**: Competition can be run start-to-finish including participant delivery

---

## Phase 13: User Story 11 - Discrepancy Consensus Alert (Priority: P2)

**Goal**: >7-point detection on submit, provisional hold, adjust-until-convergence, close block

**Independent Test**: quickstart.md scenario 11

### Tests for User Story 11 (MANDATORY — write first, must fail) ⚠️

- [ ] T079 [P] [US11] Unit tests: pairwise >7 detection (2 and ≥3 judges), `PendingConsensus` transition, resolution when all totals within 7, single-judge no-alert in `backend/tests/BirraPoint.Api.UnitTests/Evaluations/DiscrepancyTests.cs`
- [ ] T080 [P] [US11] Contract tests: divergent submit → `201 PendingConsensus` + alert; `PUT` evaluation allowed only for involved judges during open alert (`409 evaluation-locked` otherwise); close blocked with `409 discrepancy-open` in `backend/tests/BirraPoint.Api.IntegrationTests/Evaluations/DiscrepancyApiTests.cs`

### Implementation for User Story 11

- [ ] T081 [US11] Activate discrepancy detection in SubmitEvaluation; slices AdjustEvaluation (judge PUT path), GetMyDiscrepancies; `DiscrepancyRaised`/`DiscrepancyResolved` emits in `backend/src/BirraPoint.Api/Features/Evaluations/Discrepancy.cs`
- [ ] T082 [US11] Frontend discrepancy feature: alert banner + totals comparison view, adjustment flow reopening the sheet, resolved confirmation in `frontend/src/app/features/discrepancy/`
- [ ] T083 [US11] E2E scenario 11 (two judges 15 points apart → converge) in `frontend/e2e/us11-discrepancy.spec.ts`

**Checkpoint**: Consensus rules enforced; offline-raised alerts surface on next connection (edge case)

---

## Phase 14: User Story 12 - Live Judge Removal for Conflict of Interest (Priority: P3)

**Goal**: Instant removal from live table, access revoked, submitted evaluations preserved, audited

**Independent Test**: quickstart.md scenario 12

### Tests for User Story 12 (MANDATORY — write first, must fail) ⚠️

- [ ] T084 [P] [US12] Unit tests: removal sets `RemovedAt` (no hard delete with evaluations), submitted evaluations remain valid, hub group ejection call in `backend/tests/BirraPoint.Api.UnitTests/Tables/RemoveJudgeTests.cs`
- [ ] T085 [P] [US12] Contract tests: `DELETE .../tables/{tableId}/judges/{judgeId}` → subsequent judge requests for that table 404, pending sync rejected, AuditLog row written in `backend/tests/BirraPoint.Api.IntegrationTests/Tables/RemoveJudgeApiTests.cs`

### Implementation for User Story 12

- [ ] T086 [US12] Slice RemoveJudgeFromTable (revocation, `JudgeRemoved` emit to both groups, audit) + membership guard applied in every judge-workspace slice in `backend/src/BirraPoint.Api/Features/Tables/RemoveJudge.cs`
- [ ] T087 [US12] Frontend: removal action on dashboard; judge client handles `JudgeRemoved` (immediate eject, outbox items for that table surfaced as rejected) in `frontend/src/app/features/dashboard/` and `frontend/src/app/core/offline/`
- [ ] T088 [US12] E2E scenario 12 in `frontend/e2e/us12-removal.spec.ts`

**Checkpoint**: All 12 user stories independently functional

---

## Phase 15: Polish & Cross-Cutting Concerns

- [ ] T089 [P] Accessibility sweep: axe-core suite over every judge-facing and organizer route, fix violations to WCAG 2.1 AA (SC-009) in `frontend/e2e/a11y.spec.ts`
- [ ] T090 [P] Performance verification: k6 script for API budgets (reads p95 <200 ms, writes <500 ms) in `infra/perf/api-budgets.js`; assert dashboard ≤1 s and draft-save ≤300 ms timings already covered in E2E; SC-006 scale check — generated 500-row import fixture with 20% style errors resolved and consolidated in one session in `frontend/e2e/us3-import-scale.spec.ts`
- [ ] T091 [P] Frontend budgets: enforce 500 KB gzip initial bundle in `frontend/angular.json` budgets + Lighthouse PWA/TTI check (<3 s on 4G profile)
- [ ] T092 Run all 12 quickstart.md scenarios end to end; fix any docs drift in `specs/001-birrapoint-mvp/quickstart.md` (Principle X); manual usability gate for SC-010 — at least 5 first-time judges complete an evaluation sheet unaided in under 10 minutes
- [ ] T093 [P] Documentation completion: final commands/structure in `CLAUDE.md`, project overview + setup in `README.md`
- [ ] T094 Security pass: verify deny-by-default on every endpoint vs contracts/rest-api.md role matrix, secrets only via env vars, no sensitive data in logs/AuditLog payloads

---

## Phase 16: Deployment & Operations (FR-043–FR-048)

**Purpose**: Containerization, IaC, and single-command cloud deployment (spec Operations & Deployment group; constitution v1.2.0; research R-16–R-19)

- [ ] T095 [P] Backend multi-stage Dockerfile (.NET SDK build → ASP.NET runtime) in `backend/src/BirraPoint.Api/Dockerfile` and frontend multi-stage Dockerfile (Node build → Nginx Alpine) in `frontend/Dockerfile` + `frontend/nginx.conf`; verify no secrets or environment-specific config are baked into either image (FR-043)
- [ ] T096 azd deployment model: `azure.yaml` + `infra/bicep/` provisioning ACR, the ACA environment, container apps for frontend (external ingress), backend, and Keycloak (realm import), plus the PostgreSQL container with persistent volume; all configuration/secrets injected via env vars/secrets (FR-045/FR-046)
- [ ] T097 Scheduled PostgreSQL backup: ACA job running `pg_dump` export to Azure Blob Storage + documented restore procedure in `infra/backup/RESTORE.md` (FR-047)
- [ ] T098 Operations verification: health endpoints + OpenTelemetry traces/metrics/logs visible for every service locally (Aspire dashboard) and in ACA; health/telemetry assertions in `backend/tests/BirraPoint.Api.IntegrationTests/Operations/HealthTelemetryTests.cs` (FR-048)
- [ ] T099 SC-011 validation: fresh `azd up` into a clean resource group completes with zero manual steps; record the validated procedure in `specs/001-birrapoint-mvp/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS all user stories**
- **US1 (Phase 3)**: After Foundational; no story dependencies
- **US2 (Phase 4)**: After Foundational; independent
- **US3 (Phase 5)**: Needs a competition to import into (US2) for E2E; API slices independent
- **US4 (Phase 6)**: Independent (needs a competition for E2E)
- **US5 (Phase 7)**: Needs entries (US3) and judges (US4) for meaningful E2E
- **US6 (Phase 8)**: Needs tables (US5)
- **US7 (Phase 9)**: Needs fixed order (US6)
- **US8 (Phase 10)**: Needs evaluations (US7)
- **US9 (Phase 11)**: Needs evaluations flowing (US7); consumes events from US6–US8
- **US10 (Phase 12)**: Needs closed tables (US8)
- **US11 (Phase 13)**: Extends SubmitEvaluation (US7) and close gate (US8)
- **US12 (Phase 14)**: Needs live tables (US6) and dashboard action surface (US9)
- **Polish (Phase 15)**: After desired stories
- **Deployment & Ops (Phase 16)**: T095 any time after Setup; T096–T098 after Foundational (need the AppHost from T005 and running services); T099 last — it validates the releasable whole

Note: this feature's stories form a pipeline (provisioning → judging → closing), so priority order
is also the natural dependency order. Each story still has its own independent test checkpoint.

### Parallel Opportunities

- All [P] setup tasks (T002–T006) after T001
- Foundational: T010, T012, T013, T014 in parallel after T009; T019/T020 parallel to backend tasks
- Within every story: the test tasks [P] run in parallel first; backend slice vs frontend feature tasks are parallelizable across files once tests exist
- Team split after Phase 2: Dev A US2→US3→US5 (organizer chain), Dev B US1→US4, Dev C US6→US7 (judge chain)

### Parallel Example: User Story 7

```bash
# Write all US7 tests together first (must fail):
Task: "T055 Unit tests SubmitEvaluation validator in backend/tests/.../SubmitEvaluationTests.cs"
Task: "T056 Contract tests submit/replay/409s in backend/tests/.../SubmitEvaluationApiTests.cs"
Task: "T057 Jest tests SyncService in frontend/src/app/core/offline/sync.service.spec.ts"

# Then implement backend and frontend in parallel:
Task: "T058 SubmitEvaluation slice"          # backend
Task: "T059 Evaluation sheet UI" + "T060 offline engine"   # frontend
```

---

## Implementation Strategy

### MVP First

1. Phases 1–2 (Setup + Foundational) — everything depends on these
2. US1 (auth) → smallest demonstrable increment
3. Then follow the pipeline US2→US7: after US7 the **core judging loop is demoable** (login,
   provision, blind-ordered offline evaluation) — this is the recommended first demo milestone
4. US8 completes the trustworthy-results story (all P1 done)

### Incremental Delivery

Each story phase ends at a checkpoint mapped to a quickstart scenario — stop, run the scenario,
demo, continue. P2 stories (US9–US11) make the event runnable live; US12 (P3) is the last safety
valve; Phase 15 hardens budgets, accessibility, and docs before release.

### Notes

- Tests within each story MUST be written first and observed failing (Constitution Principle III)
- Commit after each task or logical group; spec artifacts travel with implementation (CLAUDE.md Git rules)
- The BR-01 anonymity contract test (T051) is the single most important regression guard in the suite — never skip it
