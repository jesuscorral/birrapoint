# Quickstart & Validation Guide: BirraPoint MVP

**Plan**: [plan.md](./plan.md) · **Contracts**: [contracts/](./contracts/) ·
**Data model**: [data-model.md](./data-model.md)

This is the end-to-end validation guide for the feature. Commands assume the structure defined in
plan.md §Project Structure; they become real once the setup tasks land and MUST then be mirrored
into `CLAUDE.md` (Principle X).

## Prerequisites

- Docker Desktop (container runtime for the Aspire-managed PostgreSQL 16, Keycloak 25+, Mailpit)
- .NET 10 SDK
- Node.js 24+ / npm 10+ (Jest loads `jest.config.ts` via Node's native TS type stripping — no ts-node)
- Azure Developer CLI (`azd`) — cloud deployment only

## Environment up

```bash
# Full local topology with one command (FR-044): .NET Aspire AppHost starts PostgreSQL,
# Keycloak (birrapoint realm auto-imported: roles ORGANIZER/JUDGE, seeded organizer),
# Mailpit (SMTP sink), the backend API (EF migrations incl. BJCP 2021 seed applied on
# startup in Development — lands with T009/T010) and the Angular frontend.
dotnet run --project backend/src/BirraPoint.AppHost
# Aspire dashboard (resources, logs, traces, health): https://localhost:17202, login URL
# printed on startup
# API: http://localhost:5121 · https://localhost:7075 (OpenAPI arrives with the first
# business endpoints) · PWA: http://localhost:4200 · Keycloak: http://localhost:8081

# Frontend-only iteration (optional, API already running):
cd frontend && npm ci && npm start
```

Configuration is environment-variable driven (no secrets in the repo): `ConnectionStrings__Db`,
`Keycloak__Authority`, `Keycloak__AdminClientId/Secret`, `Smtp__Host/Port` — supplied locally by
the AppHost, in the cloud by Bicep-provisioned env vars/secrets.

## Cloud deployment (SC-011)

```bash
azd auth login
azd up   # builds the Docker images, pushes to ACR, provisions the ACA environment via Bicep
         # (frontend public ingress, backend, Keycloak, PostgreSQL container + backup job),
         # and deploys — zero manual configuration steps
```

## Test commands

```bash
# Backend unit (handlers + validators)
dotnet test backend/tests/BirraPoint.Api.UnitTests

# Backend contract/integration (WebApplicationFactory + Testcontainers PostgreSQL)
dotnet test backend/tests/BirraPoint.Api.IntegrationTests

# Single test
dotnet test backend/tests/BirraPoint.Api.UnitTests --filter "FullyQualifiedName~SubmitEvaluation"

# Frontend unit / E2E (incl. offline + axe accessibility suites)
cd frontend && npx jest
cd frontend && npm run e2e   # = playwright test -c e2e (config lives in e2e/)
```

## End-to-end validation scenarios

Each scenario maps to a spec user story (US) and must pass before that story is Done.

| # | Scenario (US) | Steps | Expected |
|---|---------------|-------|----------|
| 1 | Access & roles (US1) | Open `:4200` unauthenticated → login as seeded organizer; repeat as invited judge | Redirect to Keycloak; organizer lands on dashboard, judge on "my tables"; judge first login forces password change with no bypass |
| 2 | Wizard & drafts (US2) | Create competition, leave step 2 via "Save draft", reopen | Draft state persisted; wizard resumes with data intact; Next disabled until name/venue/dates valid |
| 3 | Import & correction (US3) | Upload `samples/entries-with-errors.xlsx` (contract example incl. `99Z` row) | Row-level results; correction screen resolves `StyleMismatch`; consolidation blocked until resolved; blind codes generated |
| 4 | Judge invitations (US4) | Bulk-add emails incl. one duplicate | Profiles created, duplicate reported; invitation visible in Mailpit (`:8025`) |
| 5 | Tables & COI (US5) | Assign a judge who owns an assigned beer; then a judge-entrant without direct collision | `409 conflict-of-interest` with judge+entries; second case saves and flags all their entries `NotValidForBos` |
| 6 | Order & sequence (US6) | Two judge browser sessions on one table; fix order in one | Other session reorders ≤1 s and locks; sheets openable only in fixed sequence, only after order fixed and state `InEvaluation` |
| 7 | Offline evaluation (US7) | Playwright `context.setOffline(true)` mid-sheet; restart page; go online | Badge shown; draft intact after reload (≤300 ms saves); exactly one evaluation server-side after replay (idempotency) |
| 8 | Close & immutability (US8) | Complete all sheets, close table; attempt judge edit + late offline sync; organizer correction | Judge mutations `409 table-closed`; organizer edit succeeds and lands in AuditLog |
| 9 | Live dashboard (US9) | Dashboard open while judge submits | Progress updates ≤1 s, no reload; audit drill-down read-only |
| 10 | Finalize & dispatch (US10) | Close all tables, finalize | ZIP hierarchy `/Competition/Participant/Style_BlindCode.pdf`; every participant email in Mailpit; per-recipient status + retry works |
| 11 | Discrepancy (US11) | Two judges score one sample 15 pts apart | Second submit `PendingConsensus` + alert both sessions; close blocked; adjust within 7 → resolved |
| 12 | Live removal (US12) | Remove judge from live table via dashboard | Judge session ejected instantly; their sync attempts `404/409`; submitted evaluations remain in audit |

## Quality gates (constitution)

- All endpoints exercised by contract tests asserting status codes, ProblemDetails `type` URNs,
  and — for judge-facing payloads — the **absence** of entrant fields (BR-01 structural test).
- `npm run e2e -- a11y` runs axe-core on every judge-facing route as it lands (WCAG 2.1 AA,
  SC-009); today the suite covers the placeholder app shell only.
- Performance spot-checks: dashboard latency (scenario 9) and draft-save timing (scenario 7)
  asserted in E2E; API p95 budgets verified with a k6/`dotnet-counters` pass before release.
- Operations: health endpoints + OpenTelemetry visible for every service in the Aspire dashboard
  and in ACA (FR-048); a fresh `azd up` into a clean resource group completes with zero manual
  steps (SC-011); backup/restore procedure exercised once per `infra/backup/RESTORE.md` (FR-047).
