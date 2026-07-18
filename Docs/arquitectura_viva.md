# Living Architecture — BirraPoint

> Living document: it reflects the **actual current state** of the system, and MUST be updated
> at the close of every backlog task (see CLAUDE.md §Implementation workflow, step 6).
> Decisions with trade-offs are recorded in `Docs/adrs/`; the approved design lives in
> `specs/001-birrapoint-mvp/`. All documentation in this repository is written in English.

**Last updated:** 2026-07-18 · after T018 (Phase 2 Foundational, in progress)

## Global status

Phase 1 (Setup, T001–T007) is **complete**. Phase 2 (Foundational) has landed the domain model
and persistence layer (T008–T009), the full BJCP 2021 style catalog (T010), Keycloak JWT bearer
auth with a deny-by-default fallback policy (T011), the ProblemDetails exception-handler chain
for the 14-entry error catalog (T012), the MediatR + FluentValidation pipeline (T013), the
audit trail writer (T014), the `CompetitionHub` realtime skeleton + emit-after-commit
dispatcher (T015), the `DispatchJob` queue + hosted `DispatchWorker` (T016), the first REST
slice, `GET /api/v1/styles` (T017), which also closed the JWT audience-validation gap tracked
since T011 (ADR-0009) and brought OpenAPI online at `/openapi/v1.json`, and the
`WebApplicationFactory` + Testcontainers + test-JWT-issuer HTTP harness with `GetStyles`' first
contract test (T018). Verified against a real Aspire-provisioned PostgreSQL + Keycloak (all
tables present, catalog seeded, app boots with the full pipeline wired in, `/health`/`/alive`
still anonymous and green). T019–T020 (frontend core auth/services) remain in Phase 2.

## Local topology (.NET Aspire — `dotnet run --project backend/src/BirraPoint.AppHost`)

| Resource | Implementation | Local endpoint | Notes |
|---|---|---|---|
| `postgres` / database `db` | `postgres:16` container, persistent data volume, persistent lifetime | dynamic port | connection string injected into the API as `ConnectionStrings__db` |
| `keycloak` | `quay.io/keycloak/keycloak:26.2` container via `AddContainer` (ADR-0001) | http://localhost:8081 | realm `birrapoint` auto-imported from `infra/keycloak/` (roles `ORGANIZER`/`JUDGE`, seeded organizer, PKCE SPA client, admin service-account client with `manage-users`); bootstrap/realm credentials are local-dev placeholders (FR-046) |
| `mailpit` | CommunityToolkit MailPit integration | dynamic SMTP + UI ports | local mail sink for invitations/results |
| `api` | `BirraPoint.Api` project | http://localhost:5121 · https://localhost:7075 (launchSettings) | receives env: `Keycloak__Authority` (realm URL), `Keycloak__AdminClientId/Secret` (dev placeholder), `Smtp__Host/Port` (from the Mailpit endpoint); waits for the database |
| `frontend` | `npm start` (ng serve) via `AddNpmApp` | http://localhost:4200 (non-proxied) | matches the SPA client redirect URIs; waits for the API |

## Backend (`backend/`, .NET 10 / C# 14)

- **Projects** (`BirraPoint.sln`): `BirraPoint.Api` (modular monolith; `Domain/` shared kernel +
  `Common/Persistence/` now populated, `Features/ Realtime/` still empty), `BirraPoint.AppHost`
  (Aspire SDK 13.4.6), `BirraPoint.ServiceDefaults`, tests `BirraPoint.Api.UnitTests` and
  `BirraPoint.Api.IntegrationTests`.
- **`BirraPoint.Api`**: `AddServiceDefaults()` + `MapDefaultEndpoints()`, plus (T009)
  `AddDbContext<AppDbContext>` wired to the `db` connection string and `Database.MigrateAsync()`
  run on startup **in Development only**. Pipeline order (T011/T012):
  `UseExceptionHandler()` → `UseAuthentication()` → `UseAuthorization()` → endpoint mapping.
  **Current HTTP surface**: `/health` (all checks) and `/alive` (checks tagged `live`), both
  mapped in Development only (stock ServiceDefaults guard; ACA probes will need a scoped exposure
  decision in Phase 16) and explicitly `.AllowAnonymous()` since T011's deny-by-default fallback
  policy would otherwise block unauthenticated container/Aspire probes; `/openapi/v1.json` (T017,
  `AddOpenApi()`/`MapOpenApi()`); and the first business endpoint, `GET /api/v1/styles` (T017,
  any authenticated caller per the fallback policy).
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
  `CompetitionEvents` holds the 7 catalogued event-name constants; `DispatchWorker` (T016) is the
  first actual emitter (`DispatchProgress`), though no job is ever enqueued yet in practice — no
  slice calls `IDispatchJobQueue.EnqueueAsync` until T041/T075. The first story-driven emitter is
  still US2's `ChangeCompetitionState` (T028). **Known gap**: the DB-backed authorization checks
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

## Frontend (`frontend/`, Angular 20)

- Standalone components + Signals; PWA via `@angular/pwa` (`ngsw-worker.js` registered
  `registerWhenStable:30000`, enabled outside dev mode); Tailwind CSS v4 through the PostCSS
  plugin (`.postcssrc.json`); zone-based change detection for now (**zoneless under evaluation
  — ADR-0003**).
- **Feature-Sliced Design skeleton**: `src/app/core/`, `src/app/features/`, `src/app/shared/`
  (empty, `.gitkeep`). Root component is a minimal accessible shell (h1 + `router-outlet`);
  no routes defined yet.
- **Dependencies**: Angular-lockstep packages pinned to the 20.x line (`@angular/cdk@^20.2`,
  `keycloak-angular@^20.1` — ADR-0002); independent: `keycloak-js@^26.2`, `dexie@^4.4`,
  `@microsoft/signalr@^10`, `tailwindcss@^4.3`.
- **Bundle** (production build): initial total ~230.7 kB raw / ~64.8 kB transfer — within the
  ≤ 500 kB gzip budget (Principle IX); `zone.js` polyfills account for ~34.6 kB raw of it.

## Testing & quality gates

| Suite | Command | Current state |
|---|---|---|
| Backend unit + integration | `dotnet test backend/BirraPoint.sln` | green — 46 unit tests: smoke + T010 `BjcpStyleSeedDataTests` (5, DB-free catalog-shape guard) + T011 `Common/Auth` (13: claims transformation, `CurrentUser`, DI-wiring smoke) + T012 `Common/Errors` (6: exception-handler mapping/security) + T013 `Common/Behaviors` (7: `ValidationBehavior` isolation + MediatR DI-wiring end-to-end) + T015 `Realtime` (4: `CompetitionGroups` formatting, `EventPublisher` group/event/payload routing via hand-rolled `IHubContext` fakes) + T016 `Common/Jobs` (10: `DispatchRetryPolicy` — max-attempts cutoff, backoff doubling, backoff cap); 17 integration tests against a real Testcontainers PostgreSQL: smoke + 6 schema tests (T009) + 5 catalog-seed tests (T010) + T014 `AuditWriterTests` (3: atomic staging, null-before, no-save-no-persist) + T018 `Catalog/GetStylesTests` (2: 401 unauthenticated, 200 full catalog/shape/order via the new `WebApplicationFactory` + Testcontainers + test-JWT-issuer harness) |
| Frontend unit | `cd frontend && npx jest` | green — jest-preset-angular 17, jsdom, TS config via Node 24 native type stripping (no ts-node); Karma fully removed (R-13) |
| E2E + accessibility | `cd frontend && npm run e2e` (`playwright test -c e2e`) | green — smoke spec + `e2e/a11y` axe-core WCAG 2.1 A/AA gate (SC-009); **chromium only** — a webkit/mobile-Safari project is pending before the offline suites |
| Lint / format | `ng lint` (angular-eslint flat config incl. template accessibility rules), `npm run format:check` (Prettier), `dotnet format --verify-no-changes` (backend/.editorconfig) | clean — T007 set Prettier `endOfLine: "auto"`: the gate had been red on every Windows checkout because git autocrlf smudges the tree to CRLF while Prettier defaults to `lf` |

## Data flows

`GET /api/v1/styles` (T017) is the first live REST endpoint: any authenticated caller (JWT bearer,
audience `birrapoint-api`) → `GetStylesQuery` via MediatR → `GetStylesQueryHandler` reads
`AppDbContext.BjcpStyles` → `200` with the lightweight `[{ code, name, categoryNumber,
categoryName }]` catalog projection. `CompetitionHub` (T015) is mapped at `/hubs/competition` and
accepts authenticated group joins; `DispatchWorker` (T016) is running and would emit
`DispatchProgress` on any job status change, but no story enqueues a `DispatchJob` yet, so in
practice nothing flows over the hub until US2's `ChangeCompetitionState` (T028) becomes the first
real emitter. The database holds the full domain schema (T008–T009); target contracts live in
`specs/001-birrapoint-mvp/contracts/` (REST `/api/v1`, SignalR `CompetitionHub`, `.xlsx` import
file).

## Recorded debt / immediate next steps

- **ADR-0003**: decide zoneless change detection before Phase 3 frontend work.
- **ADR-0004**: domain state/status enums are stored as strings in PostgreSQL (T009).
- **ADR-0006**: `CompetitionHub`'s DB-backed join-authorization (ownership/membership checks) has
  no integration/contract test yet. The `WebApplicationFactory` harness it needs now exists
  (T018) — no task has written this coverage yet.
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
