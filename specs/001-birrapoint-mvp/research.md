# Phase 0 Research: BirraPoint MVP

**Date**: 2026-07-06 | **Plan**: [plan.md](./plan.md)

All Technical Context unknowns are resolved below. Format per decision: Decision / Rationale /
Alternatives considered.

## R-01: .NET version — 10 (LTS)

- **Decision**: .NET 10 with C# 14.

## R-02: Angular version — 20.x

- **Decision**: Angular 20 (latest stable satisfying the "17+" constraint at scaffold time).
- **Rationale**: Standalone components and Signals are stable defaults; long remaining support
  window; `@angular/pwa` and CDK fully supported.
- **Alternatives considered**: Pinning 17 — older, no benefit; whatever-is-latest floating —
  rejected, plans must pin versions for reproducibility.

## R-03: MediatR — pin 12.x (Apache-2.0)

- **Decision**: MediatR 12.5.x.
- **Rationale**: The constitution names MediatR for slice orchestration. Versions ≥ 13 moved to a
  commercial license; 12.x is the last fully open line, sufficient for request/handler +
  pipeline-behavior usage (validation behavior, logging).
- **Alternatives considered**: MediatR 13+ (license cost unjustified for MVP); Wolverine
  (heavier, unneeded messaging features); hand-rolled mediator (reinventing a wheel the
  constitution already standardized on).

## R-04: Excel parsing — ClosedXML

- **Decision**: ClosedXML (MIT) for `.xlsx` row-by-row reading in the Import slice.
- **Rationale**: Free, no COM interop, streaming-friendly for ≤ ~500-row files, actively
  maintained. Named in the approved stack input.
- **Alternatives considered**: EPPlus (commercial license since v5); NPOI (clunkier API);
  OpenXML SDK raw (too low-level for the schedule).

## R-05: PDF generation — QuestPDF Community

- **Decision**: QuestPDF, Community license.
- **Rationale**: Code-first fluent layout suits generated score sheets; Community license is free
  for organizations under $1M revenue — BirraPoint MVP qualifies. Named in the approved stack.
- **Alternatives considered**: iText (AGPL/commercial); wkhtmltopdf (unmaintained, process
  dependency); browser-side printing (cannot run in the background dispatch job).

## R-06: Background dispatch — hosted worker + DB-persisted job queue

- **Decision**: A .NET `BackgroundService` consuming a `DispatchJob` table (PostgreSQL) fed by the
  Dispatch slice; in-process `System.Threading.Channels` for wake-up signaling. Job types:
  `GeneratePdfs`, `BundleZip`, `SendResultEmail`, `SendInvitation`. Jobs are idempotent and
  resumed on startup (any `Pending`/`Running` job re-queues).
- **Rationale**: FR-036 requires background PDF generation without blocking; the constitution
  (KISS, minimal deps) rules out a broker for an MVP hosting a single live event. DB persistence
  gives restart-safety, retry counts, and per-recipient status (FR-041) for free with EF Core.
- **Alternatives considered**: Hangfire (extra dependency + dashboard surface for marginal gain);
  Azure Storage Queues/Service Bus (cloud lock-in, breaks local Docker story); fire-and-forget
  `Task.Run` (loses jobs on restart — unacceptable for result dispatch).

## R-07: Idempotent evaluation sync

- **Decision**: Client sends `X-Idempotency-Key` = `{competitionId}:{tableId}:{judgeId}:{entryId}`
  (deterministic, matching BR-04). Server enforces a unique index on `(JudgeId, BeerEntryId)`;
  a replay returns `200 OK` with the stored evaluation instead of `201 Created`. No generic
  idempotency-key store needed because the key is fully derivable from the resource identity.
- **Rationale**: Satisfies FR-029 with one unique constraint; deterministic keys mean offline
  retries across app restarts still dedupe.
- **Alternatives considered**: Random-GUID idempotency keys + key store (extra table and TTL
  logic for zero benefit given natural uniqueness); UPSERT semantics (would silently overwrite,
  violating the locked-on-submit rule from Clarification Q2).

## R-08: Offline engine — Dexie outbox + `online` event replay

- **Decision**: Two Dexie stores: `drafts` (keyed `entryId`, written ≤300 ms after each change)
  and `outbox` (submitted-but-unsynced evaluations). A `SyncService` (Angular, DI singleton)
  replays the outbox on `window: online`, on app start, and after each successful submit;
  exponential backoff on 5xx; item removed on 2xx or on 4xx that proves server-side existence
  (replay 200) or permanent rejection (table closed → surfaced to user per edge case).
- **Rationale**: Matches the approved stack input, which explicitly avoids the Background Sync
  API for compatibility (notably iOS Safari). Draft persistence and outbox survive app restarts
  (SC-003).
- **Alternatives considered**: Service Worker Background Sync API (unsupported on iOS Safari —
  judges bring their own devices); localStorage (synchronous, size limits, no indexing);
  full CRDT sync (massively over-engineered for append-mostly evaluations).

## R-09: Real-time — SignalR `CompetitionHub` with role-scoped groups

- **Decision**: Single hub `CompetitionHub` (constitutional name; supersedes the `LiveProgressHub`
  name that appeared in early acceptance-criteria input). Groups:
  `competition:{id}:organizers` (join guarded by `ORGANIZER` role) and `table:{tableId}` (join
  guarded by table membership). Server-to-client events only; all mutations go through REST.
  JWT auth via `access_token` query param on the WebSocket handshake (standard SignalR pattern).
- **Rationale**: Group-per-audience keeps anonymity guarantees enforceable — judge-group payloads
  are built from blind DTOs only. REST-only mutations keep contract-first (Principle VI) intact.
- **Alternatives considered**: Two hubs (organizer/judge) — more surface, no isolation benefit
  groups don't already give; WebSocket raw / SSE — reinvents SignalR features (reconnect, groups).

## R-10: Judge provisioning & invitations — Keycloak Admin API + app-side SMTP

- **Decision**: The Judges slice calls the Keycloak Admin REST API (service account with
  `manage-users`) to create users with a generated temporary password and required action
  `UPDATE_PASSWORD`. Invitation emails (branded, with credentials and competition context) are
  sent by the backend via MailKit/SMTP and tracked per recipient (FR-014/FR-041). Local dev uses
  Mailpit; production uses any SMTP relay via configuration.
- **Rationale**: Keycloak owns credentials and the forced-change flow (AC-1.1.2) natively;
  app-side email gives delivery status visibility and retries the spec requires, which Keycloak's
  built-in `execute-actions-email` does not expose.
- **Alternatives considered**: Keycloak `execute-actions-email` (no per-recipient status/retry
  control, unbranded); building password flows in-app (violates Principle VII — Keycloak-only
  auth).

## R-11: Frontend OIDC — `keycloak-angular` + `keycloak-js`

- **Decision**: `keycloak-angular` (with the official `keycloak-js` adapter), Authorization Code
  + PKCE, silent token refresh; route guards map realm roles to `/organizer/**` and `/judge/**`
  route trees.
- **Rationale**: Official adapter semantics (login redirects, required-action handling — the
  forced password change simply happens inside the Keycloak-hosted flow, satisfying AC-1.1.2's
  no-bypass requirement because the app never receives tokens until actions complete).
- **Alternatives considered**: `angular-auth-oidc-client` (fine generically, but required-action
  behavior is exactly the Keycloak-specific part we need first-class); hand-rolled OIDC
  (security-sensitive wheel reinvention).

## R-12: BJCP 2021 catalog — seeded read-only reference data

- **Decision**: Ship the BJCP 2021 style list (category number/name, style code/name) as a JSON
  resource seeded through an EF Core migration; exposed via `GET /api/v1/styles`; never mutated
  at runtime.
- **Rationale**: FR-012 requires a preloaded master catalog; seeding via migration keeps every
  environment identical and testable.
- **Alternatives considered**: Fetching from bjcp.org at runtime (availability/licensing risk,
  breaks offline dev); manual SQL script (bypasses migration history).

## R-13: Testing stack

- **Decision**: Backend — xUnit; slice handler/validator unit tests; integration/contract tests
  with `WebApplicationFactory` + Testcontainers-for-.NET (real PostgreSQL), auth via test JWT
  issuer. Frontend — Jest via `jest-preset-angular` (Karma is deprecated); Playwright E2E with
  offline-network simulation (context.setOffline) and `@axe-core/playwright` for WCAG checks.
- **Rationale**: Testcontainers exercises real Npgsql/EF behavior (unique constraints, jsonb) that
  in-memory providers fake; Playwright's network control is the only realistic way to test the
  offline engine end to end (SC-002); axe automates Principle VIII gates.
- **Alternatives considered**: EF InMemory provider (lies about relational behavior); Karma/
  Jasmine (deprecated); Cypress (weaker multi-tab/offline control than Playwright).

## R-14: PDF sheet content & anonymity boundary

- **Decision**: Participant-facing PDFs include: competition name, blind code, style, per-section
  scores and comments, total, consolidated (mean) score, and the evaluating judge's display name
  (standard BJCP scoresheet practice). Judge-facing surfaces still never show entrant data;
  entrant-facing PDFs revealing judge names is a one-way disclosure that does not violate BR-01.
- **Rationale**: Brewers get actionable feedback and judges are accountable — mirrors paper BJCP
  score sheets; BR-01 protects entrant identity from judges, not judge identity from entrants.
- **Alternatives considered**: Anonymous judge sheets (reduces feedback credibility, diverges
  from BJCP convention; can be revisited post-MVP if judges object).

## R-15: API versioning & error shape

- **Decision**: URL-versioned base path `/api/v1`; every error is RFC 7807 `application/problem+json`
  with stable `type` slugs (e.g. `urn:birrapoint:conflict-of-interest`,
  `urn:birrapoint:table-closed`, `urn:birrapoint:discrepancy-open`, `urn:birrapoint:invalid-state-transition`).
  Domain conflicts use HTTP 409; validation failures use 400 with field errors; state-gate
  violations use 409.
- **Rationale**: Principle VI mandates ProblemDetails and versioned, contract-first changes;
  stable `type` URNs let the frontend switch on machine-readable causes (e.g., showing the COI
  dialog vs. a generic toast).
- **Alternatives considered**: Header versioning (harder to exercise from curl/docs); ad-hoc error
  JSON (violates constitution).

## R-16: Local orchestration — .NET Aspire *(added 2026-07-07)*

- **Decision**: `BirraPoint.AppHost` orchestrates the entire local topology (PostgreSQL, Keycloak
  with realm import, Mailpit, the API, and the Angular frontend as an npm resource);
  `BirraPoint.ServiceDefaults` injects OpenTelemetry, health checks, and resilience defaults into
  every .NET service. One command starts everything (FR-044); the Aspire dashboard provides
  logs/traces/health (FR-048).
- **Rationale**: Constitution v1.2.0 (amendment sourced from spec Clarifications 2026-07-07 Q1)
  replaced Docker Compose: one orchestrator, dev/prod topology parity, and the same AppHost model
  drives azd deployment generation.
- **Alternatives considered**: Docker Compose (superseded by amendment — second topology
  definition would drift); Tilt/Skaffold (Kubernetes-shaped, wrong target).

## R-17: Deployment — Bicep via Azure Developer CLI to Azure Container Apps *(added 2026-07-07)*

- **Decision**: `azure.yaml` + `infra/bicep/` (generated/extended from the AppHost model) so that
  a single `azd up` builds the multi-stage images, pushes to Azure Container Registry, provisions
  the ACA environment, and deploys frontend (public ingress), backend, and Keycloak (FR-045/046).
- **Rationale**: Constitution v1.2.0 fixes the target; azd's Aspire integration is the shortest
  path to the single-command requirement (SC-011) with zero manual configuration.
- **Alternatives considered**: Terraform (constitution chose Bicep); hand-rolled GitHub Actions
  pipeline first (post-MVP concern — azd works locally and from CI later).

## R-18: Production PostgreSQL — in-environment container + scheduled backups *(added 2026-07-07)*

- **Decision**: PostgreSQL runs as a container app in the ACA environment with persistent volume
  storage (Azure Files); a scheduled ACA job runs `pg_dump` exports to Azure Blob Storage, and the
  restore procedure is documented in `infra/backup/` (FR-047).
- **Rationale**: User decision (spec Clarifications 2026-07-07 Q2) — lowest cost, single-command
  topology. The managed-service trade-off (automated backups/PITR/HA) is explicitly compensated by
  the mandatory backup job; results data loss is the top operational risk otherwise.
- **Alternatives considered**: Azure PostgreSQL Flexible Server (recommended for durability,
  declined for MVP cost); Neon/Supabase (splits topology across providers, weakens `azd up`).

## R-19: Production Keycloak — container app in the same ACA environment *(added 2026-07-07)*

- **Decision**: Keycloak as a container app beside backend/frontend, realm imported at startup
  from `infra/keycloak/birrapoint-realm.json`, persistence in a dedicated database on the same
  PostgreSQL container; public ingress for login flows.
- **Rationale**: User decision (Clarifications 2026-07-07 Q3) — keeps one `azd up`, identical
  OIDC configuration shape between local Aspire and production.
- **Alternatives considered**: External managed Keycloak (extra vendor, breaks single-command
  provisioning); Microsoft Entra External ID (re-plan of US1/US4 and a constitution rewrite).

## Dependency justification summary (Principle V gate)

| Dependency | Slice | Why the stack can't already do it |
|------------|-------|-----------------------------------|
| ClosedXML | Import | BCL has no `.xlsx` reader |
| QuestPDF | Dispatch | BCL has no PDF layout engine |
| MailKit | Judges/Dispatch | `System.Net.Mail.SmtpClient` is deprecated by Microsoft guidance |
| Testcontainers | Tests | Real PostgreSQL behavior in CI without manual setup |
| keycloak-angular/keycloak-js | Frontend core | OIDC + Keycloak required-actions handling |
| @microsoft/signalr | Frontend core | SignalR wire protocol client |
| Dexie.js | Frontend core | Named in constitution (IndexedDB ergonomics) |
| jest-preset-angular, Playwright, @axe-core/playwright | Tests | Deprecated Karma replacement; offline + a11y E2E |

Everything else (`MediatR`, `FluentValidation`, EF Core/Npgsql, SignalR, Tailwind, CDK,
`@angular/pwa`, .NET Aspire AppHost/ServiceDefaults, Docker, Bicep/azd) is already mandated by the
constitution's Technology Constraints (v1.2.0).
