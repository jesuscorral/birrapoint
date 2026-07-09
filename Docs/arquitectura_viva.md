# Living Architecture — BirraPoint

> Living document: it reflects the **actual current state** of the system, and MUST be updated
> at the close of every backlog task (see CLAUDE.md §Implementation workflow, step 6).
> Decisions with trade-offs are recorded in `Docs/adrs/`; the approved design lives in
> `specs/001-birrapoint-mvp/`. All documentation in this repository is written in English.

**Last updated:** 2026-07-09 · after T010 (Phase 2 Foundational, in progress)

## Global status

Phase 1 (Setup, T001–T007) is **complete**. Phase 2 (Foundational) has landed the domain model
and persistence layer (T008–T009) plus the full BJCP 2021 style catalog (T010): 14 entities + 7
enums, `AppDbContext` with the InitialCreate + AddBjcpStyleCatalogDetails migrations, applying on
startup in Development — verified against a real Aspire-provisioned PostgreSQL (all tables
present, catalog seeded, `/health` green). **There is still no authentication, MediatR pipeline,
realtime hub, or business API endpoint** (T011–T020 remain in Phase 2).

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
  run on startup **in Development only**. **Current HTTP surface**: infrastructure only —
  `/health` (all checks) and `/alive` (checks tagged `live`), both mapped in Development only
  (stock ServiceDefaults guard; ACA probes will need a scoped exposure decision in Phase 16).
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
- **`Features/Catalog/Data/`** (T010): `bjcp-2021.json` — the full BJCP 2021 catalog, 125
  entries (categories 1–34 + Appendix B local styles X1–X5), marked `EmbeddedResource` in the
  csproj so it ships inside the compiled assembly (available identically in dev, CI/Testcontainers,
  and containers, regardless of working directory). `BjcpStyleCatalogLoader` reads it via
  `Assembly.GetManifestResourceStream`; `BjcpStyleSeedRecord`/`VitalStatisticsSeed`/
  `StyleDescriptionSeed` are the deserialization DTOs. The `AddBjcpStyleCatalogDetails`
  migration's `Up()` calls the loader and seeds all 125 rows via `migrationBuilder.InsertData`
  (ADR-0005) — the JSON file is the only place the catalog content itself lives; the migration
  never hardcodes it.
- **`BirraPoint.ServiceDefaults`**: OpenTelemetry (ASP.NET Core, HttpClient and runtime
  instrumentation; OTLP exporter switched by `OTEL_EXPORTER_OTLP_ENDPOINT`), default health
  checks (`self`/`live`), HttpClient resilience handler + service discovery.
- **Key packages** (pinned in the csproj): MediatR **12.5.0** (never upgrade to 13+ — license,
  R-03), FluentValidation 12.1.1, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2 +
  Microsoft.EntityFrameworkCore.Design 10.0.4 (build-time only, T009), ClosedXML 0.105.0,
  QuestPDF 2026.7.0 (requires `QuestPDF.Settings.License = LicenseType.Community` at startup —
  pending, Dispatch slice), MailKit 4.17.0.
- **Test harnesses**: xUnit in both test projects; the integration project additionally carries
  Testcontainers.PostgreSql 4.13.0 + Microsoft.AspNetCore.Mvc.Testing. `Persistence/
  SchemaTests.cs` (T009) spins up a real `postgres:16` Testcontainer, applies the migration,
  and asserts the constraints above end-to-end; the `WebApplicationFactory` HTTP-level harness
  still arrives with T018 (which must also add `public partial class Program;` to the API).

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
| Backend unit + integration | `dotnet test backend/BirraPoint.sln` | green — 5 unit tests (smoke + T010 `BjcpStyleSeedDataTests`, DB-free catalog-shape guard); 12 integration tests against a real Testcontainers PostgreSQL: smoke + 6 schema tests (T009) + 5 catalog-seed tests (T010, `BjcpStyleSeedTests` — row count, vital stats, synthetic-code entries, jsonb validity); HTTP-level harness in T018 |
| Frontend unit | `cd frontend && npx jest` | green — jest-preset-angular 17, jsdom, TS config via Node 24 native type stripping (no ts-node); Karma fully removed (R-13) |
| E2E + accessibility | `cd frontend && npm run e2e` (`playwright test -c e2e`) | green — smoke spec + `e2e/a11y` axe-core WCAG 2.1 A/AA gate (SC-009); **chromium only** — a webkit/mobile-Safari project is pending before the offline suites |
| Lint / format | `ng lint` (angular-eslint flat config incl. template accessibility rules), `npm run format:check` (Prettier), `dotnet format --verify-no-changes` (backend/.editorconfig) | clean — T007 set Prettier `endOfLine: "auto"`: the gate had been red on every Windows checkout because git autocrlf smudges the tree to CRLF while Prettier defaults to `lf` |

## Data flows

No HTTP endpoints yet. The database now holds the full domain schema (T008–T009); target
contracts live in `specs/001-birrapoint-mvp/contracts/` (REST `/api/v1`, SignalR
`CompetitionHub`, `.xlsx` import file) and the first slice proving the pipeline
(`GET /api/v1/styles`) lands with T017.

## Recorded debt / immediate next steps

- **ADR-0003**: decide zoneless change detection before Phase 3 frontend work.
- **ADR-0004**: domain state/status enums are stored as strings in PostgreSQL (T009).
- `WaitFor` a *ready* Keycloak once auth is wired (T011; ADR-0001 mitigation).
- `Aspire.Hosting.NodeJs` is on the old version train (9.5.2); align when a 13.x ships.
- Add a webkit Playwright project before writing the offline E2E suites (iOS Safari is the
  constrained target for the offline engine, R-08).
- No `.gitattributes` in the repo: Prettier `endOfLine: "auto"` (T007) keeps `format:check`
  green on both CRLF (Windows autocrlf) and LF checkouts, but a contributor with
  `core.autocrlf=false` on Windows could still commit CRLF blobs unnoticed. Durable fix:
  `* text=auto eol=lf` + Prettier `endOfLine: "lf"` as its own follow-up task (PR #3 review).
- `/health`//`/alive` exposure strategy for ACA probes (Phase 16).
