# Living Architecture — BirraPoint

> Living document: it reflects the **actual current state** of the system, and MUST be updated
> at the close of every backlog task (see CLAUDE.md §Implementation workflow, step 6).
> Decisions with trade-offs are recorded in `Docs/adrs/`; the approved design lives in
> `specs/001-birrapoint-mvp/`. All documentation in this repository is written in English.

**Last updated:** 2026-07-23 · after T072–T078 — **Phase 12 (US10, Event Closing with Automated Results Dispatch) complete**

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
`409 discrepancy-open { blindCodes }` — always empty today, same forward-declared-but-real pattern
as everywhere else discrepancy detection (US11) hasn't landed yet), then flips
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
  `IHttpContextAccessor`. Since T015, `AddKeycloakAuthentication` also wires
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
  and `JudgeTableSummaryDto` structurally carry no entrant field, and a contract test asserts the
  serialized wire payload directly, not just the DTO's declared members. `JudgeTableAccess`
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
  standard `FixOrder`'s test set. `Status` is unconditionally `Confirmed` for now — discrepancy
  detection (>7-point spread → `PendingConsensus` + `DiscrepancyAlert`) activates in US11 (Phase
  13), left as a one-line comment rather than a premature pluggable-hook abstraction. Emits
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
  `DiscrepancyAlert`s (`409 discrepancy-open { blindCodes }` — vacuously empty today, US11 isn't
  built). On success, one handler emits **two different `TableClosed` payloads** to two different
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
  - `keycloak.providers.ts`: `provideAppKeycloak()` — `initOptions: { onLoad: 'login-required',
    pkceMethod: 'S256' }`. `login-required` blocks the entire app pre-render until authenticated,
    which is how FR-001 ("redirect unauthenticated users... before showing any content") is
    satisfied — there's no public/anonymous section of this PWA, so no separate route-level auth
    guard was needed on top of it. `features: [withAutoRefreshToken()]` gives silent token
    refresh driven by user activity (R-11). **Library quirk worked around here**:
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
    checks `authData.grantedRoles.realmRoles`. Since `login-required` already guarantees
    authentication before any guard runs, these only branch on role. **T024**: a mismatch now
    redirects to the caller's *own* role landing via the new `role-landing.ts`'s
    `resolveRoleLandingUrlTree(authData)` (e.g. a JUDGE hitting `/organizer/**` lands on
    `/judge/tables`, not a dead end at root) — `parseUrl('/')` is now only the fallback for a
    caller holding neither role.
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
- **`features/competition-wizard/`** (T029, US2): a 2-step organizer wizard —
  `BasicsStepComponent` (name/venue/startDate/endDate, the FR-007 required fields, `endDate >=
  startDate` cross-field validator, `Next` disabled until valid) →
  `DetailsStepComponent` (description/logoUrl/entryLimit/registrationStart/registrationEnd, all
  optional, `Save Draft` disabled until the entry-limit/registration-window validators pass).
  `CompetitionWizardComponent` (`competition-wizard.component.ts`) drives the step switch off a
  `currentStep` signal and owns the create-vs-resume branch: with no `:id` route param it starts
  at step 1 with an empty form; with one, its constructor calls `CompetitionsApiService.getById`
  and passes the result down as `initialValue` to both steps (each has its own `effect()` that
  `patchValue`s the form when that input arrives). Deliberately **no client-side draft store** —
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
- **`features/entry-import/`** (T036, US3): one signal-driven view swap — upload → row
  results/correction → consolidate summary — rather than a full stepper, since the three views
  never need independent back-and-forth navigation beyond the natural upload-once,
  correct-until-clean, consolidate-once flow. `EntryImportApiService`
  (`entry-import-api.service.ts`) mirrors `CompetitionsApiService`'s thin-wrapper shape; `upload()`
  builds a `FormData` (field name `file`) and leaves the multipart `Content-Type` to `HttpClient`.
  `StylePickerComponent` is a small filter-as-you-type catalog picker (plain Signals, no
  `ReactiveFormsModule` needed for two local text/select values) used inline in the correction
  table for `StyleMismatch`/`Invalid` rows, alongside an `Exclude` button; row resolution updates
  local state directly from the `PUT` response (no refetch). Route: `competitions/:id/import`
  under the existing `organizer` guard, reachable only by direct navigation (no dashboard link
  yet, matching how `us2-wizard.spec.ts` already reaches the wizard). **Bug found by its own E2E
  spec, fixed same-day**: the upload `<form (ngSubmit)="onUpload()">` initially shipped without
  `FormsModule` in the component's `imports`, so Angular never bound `NgForm` to intercept the
  native `submit` event — clicking "Upload" did a real browser GET form submission, reloading the
  page and, under this app's Keycloak `login-required` init, re-triggering the entire OIDC
  redirect and wiping all state. Invisible to the original Jest spec (it called `onUpload()`
  directly, bypassing the form); caught only by T037's real-browser E2E run. Fixed by adding
  `FormsModule`; a regression test dispatching a real `submit` event now guards it.
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
| Backend unit + integration | `dotnet test backend/BirraPoint.sln` | green — 179 unit tests (was 177; +2 **T072** `Dispatch/DispatchPathsTests.cs`: the FR-040 ZIP path scheme, incl. a competition name with "awkward" characters, proving no sanitization is needed for a ZIP entry name) against smoke + T010 `BjcpStyleSeedDataTests` (5) + T011 `Common/Auth` (6) + T012 `Common/Errors` (6) + T013 `Common/Behaviors` (7) + T015 `Realtime` (4) + T016 `Common/Jobs` (10) + T021 `Auth` + T025 `Competitions/CompetitionValidatorsTests` (23) + T031 `Import/` (22) + T038 `Judges/` (15) + T045 `Tables/` (13) + T050 `TastingOrder/` (10) + T055 `Evaluations/SubmitEvaluationTests.cs` (37) + T062-T063 `Evaluations/CloseTableTests.cs` (9); 150 integration tests (was 144; +6 **T073** `Dispatch/DispatchApiTests.cs`: the pre-existing-but-previously-untested `tables-still-open` Finalize gate exercised at the HTTP level for the first time, Finalize enqueuing `GeneratePdfs`, a real end-to-end run of the full `GeneratePdfs→BundleZip→SendResultEmail` pipeline against the live `DispatchWorker`/`FakeEmailSender` asserting the ZIP's exact entry paths and every participant's email attachment, `202` before the archive is ready, retry resetting a failed job and it being reprocessed by the worker's safety-net poll — the slowest test in the suite by a wide margin (~35s, deterministic, not flaky) since it waits out that same poll interval on purpose, and three-endpoint 404 ownership scoping) against a real Testcontainers PostgreSQL: smoke + 6 schema tests (T009) + 5 catalog-seed tests (T010) + T014 `AuditWriterTests` (3) + T018 `Catalog/GetStylesTests` (2) + T021 `Auth/AuthPolicyTests` (4) + T023 `Auth/JudgeResolverTests` (4) + T026 `Competitions/CompetitionsApiTests` (14) + T032 `Import/ImportApiTests` (26) + T039 `Judges/JudgesApiTests` (16) + T046 `Tables/` (22) + T051 `TastingOrder/` (9) + T056 `Evaluations/SubmitEvaluationApiTests.cs` (12) + T057B `Catalog/GetStyleDetailTests.cs` (2) + T062-T065 `Evaluations/CloseTableApiTests.cs` (9) + T068 `Monitoring/MonitoringApiTests.cs` (8) |
| Frontend unit | `cd frontend && npx jest` | green — 290 tests (was 262; +28 **T077**: new `core/api/blob-text.spec.ts` (2), `core/api/dispatch-api.service.spec.ts` (5), `features/results-dispatch/results-dispatch.component.spec.ts` (15: status table rendering, retry/retry-all gating, download success/not-ready/error, live `DispatchProgress` pipeline-stage text), `core/api/api-client.service.spec.ts`'s `getBlob()` extension (3), plus `competition-monitor.component.spec.ts`'s new "Results & Dispatch" link coverage (2, shown/hidden by `Finalized` state)) against smoke (2) + T019 `core/auth` (10) + T020 `core/api` (8) + T020 `core/realtime` (5) + T020 `core/offline` (3) + T024 `core/auth`/`features/auth` (13) + T029 `features/competition-wizard/` (24) + T036 `features/entry-import/` (19) + T043 `features/judge-management/` (12) + T048 `features/table-management/` (46) + T053 `features/judge-tables/` (23) + T100/T102 `features/dashboard/` (19) + T057/T059/T060/T060B/T061 US7 work (48) + T066-T067 US8 work (13) + T070 US9 work (16). jest-preset-angular 17, jsdom, TS config via Node 24 native type stripping (no ts-node); Karma fully removed (R-13) |
| E2E + accessibility | `cd frontend && npm run e2e` (`playwright test -c e2e`) | **mixed, unchanged shape from T024** — `us1-auth.spec.ts` (3), `us2-wizard.spec.ts` (1), `us3-import.spec.ts` (1), `us4-judges.spec.ts` (1), `us5-tables.spec.ts` (1), `us6-order.spec.ts` (1), `us13-dashboard.spec.ts` (3), `us7-offline.spec.ts` (1), `us8-close.spec.ts` (1), `us9-dashboard.spec.ts` (1), and new **T078 `us10-dispatch.spec.ts`** (1: full US10 setup through the real dashboard/UI — finalizes a competition with a closed table, confirms the badge updates immediately with no wait on the background pipeline (Acceptance Scenario 1's "stays responsive"), polls the new dispatch screen until all three of the fixture's participants — including the one never assigned to any table, proving dispatch scope is competition-wide not table-wide — reach `Completed`, downloads the ZIP via a captured Playwright `download` event and inspects its entries with `adm-zip` (new test-only devDependency, Node has no built-in ZIP reader) against the exact FR-040 path, and confirms every participant's Mailpit message has the right PDF attachment; the retry button's *positive* case — correctly absent once everything is `Completed` — is asserted here, while the retry *mechanism* itself is covered at the integration level by `DispatchApiTests.cs` rather than forcing a contrived real-SMTP failure) — all green against a live, fully-warmed Aspire stack (also re-verified alongside `us6-order.spec.ts`/`us8-close.spec.ts`/`us9-dashboard.spec.ts`/`us13-dashboard.spec.ts` as a regression spot-check), but `smoke.spec.ts` and `e2e/a11y/home.a11y.spec.ts` still fail deterministically (pre-existing since T024, unrelated: `login-required` races `page.goto('/')`). **Gap still open**: no judge- or organizer-facing route, including the evaluation sheet and all three dashboard/results screens, is in the axe-core sweep yet — `a11y/home.a11y.spec.ts` only covers the placeholder app shell; a full-suite sweep is Phase 15/T089 territory. See Recorded debt below. Chromium only |
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
file). Frontend-side: any route load triggers Keycloak's `login-required` flow first (PKCE
redirect to the realm's hosted login if no session). **T024**: once authenticated, `''` resolves
via `homeRedirectGuard` and `/organizer`/`/judge` via `organizerGuard`/`judgeGuard`, all sharing
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
- **New**: `/organizer/dashboard` (T100), the evaluation sheet (T059), and every other organizer/
  judge route are not yet covered by the axe-core accessibility sweep — `frontend/e2e/a11y/
  home.a11y.spec.ts` only exercises the placeholder app shell today. Same gap category as the
  pre-existing `smoke.spec.ts`/`home.a11y.spec.ts` `login-required`-race failures noted below; a
  real organizer- and judge-route sweep is Phase 15/T089's job, not done piecemeal per story so far.
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
- **New**: `frontend/e2e/smoke.spec.ts` and `frontend/e2e/a11y/home.a11y.spec.ts` fail
  deterministically against a live Keycloak stack with `login-required` active — discovered while
  verifying T024, confirmed pre-existing (reproduces identically on the pre-T024 baseline via
  `git stash`), not caused by Phase 3. Both need to either drive a real login first or assert
  against the Keycloak redirect itself, mirroring `us1-auth.spec.ts`'s pattern; unfixed as of this
  update since it's outside T021–T024's scope.
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
