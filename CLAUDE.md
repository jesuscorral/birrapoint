# Project Instructions

Guidance for Claude Code (claude.ai/code) when working in this repository.

## Project

BirraPoint is a PWA for running beer competitions with blind tastings (catas a ciegas): organizer
provisioning (competition wizard with drafts, `.xlsx` entry import validated against the BJCP 2021
catalog, bulk judge invitations, tasting tables with conflict-of-interest protection), an
offline-first judge evaluation flow with a shared fixed tasting order and BJCP score caps,
discrepancy consensus, a real-time organizer dashboard, and immutable closing with automated
PDF/ZIP/email dispatch. Best of Show and tie-breaks are out of scope (only the `NotValidForBos`
flag is recorded).

**Status: Phase 1 (Setup, T001–T007) complete.** Both stack skeletons, the test harnesses, and
the one-command Aspire local topology exist and run; there is no domain model, authentication,
or business endpoint yet — implementation continues at `tasks.md` Phase 2 (Foundational,
T008–T020). `Docs/arquitectura_viva.md` tracks the actual current system state. `Docs/` holds
the original product definition (in Spanish); the English spec supersedes it.

## Source of truth (in priority order)

1. `.specify/memory/constitution.md` — v1.2.0, ten principles. Supersedes everything, including
   this file. Stack deviations require a constitution amendment, not a per-feature choice.
2. `specs/001-birrapoint-mvp/spec.md` — user stories US1–US12, FR-001–FR-048, clarifications,
   edge cases, success criteria SC-001–SC-011.
3. `specs/001-birrapoint-mvp/plan.md` — technical approach, project structure, constitution gate.
4. `specs/001-birrapoint-mvp/tasks.md` — 16 phases, dependency-ordered, grouped by user story.
5. Supporting design: `research.md` (decisions R-01–R-19 with rationale), `data-model.md`
   (entities, state machines, indexes, Dexie stores), `contracts/` (`rest-api.md`,
   `signalr-hub.md`, `import-file.md`), `quickstart.md` (validation scenarios 1–12, one per story).

Never implement functionality without an approved spec, plan, and task. Requirement changes
discovered mid-implementation flow back into the spec first — never silently overridden in code.

## Spec Kit workflow

Feature work goes through these skills in order: `/speckit-constitution` → `/speckit-specify` →
`/speckit-clarify` → `/speckit-plan` → `/speckit-tasks` → task review → `/speckit-implement`.
Supporting: `/speckit-analyze` (cross-artifact consistency), `/speckit-checklist`,
`/speckit-converge` (diff codebase vs. spec, append remaining tasks), `/speckit-taskstoissues`.

- Helper scripts are **PowerShell only**: `.specify/scripts/powershell/`. Feature numbering is
  sequential. `.specify/feature.json` points downstream commands at the active feature
  (`specs/001-birrapoint-mvp/`).
- `.specify/extensions.yml` runs `speckit.agent-context.update` after specify and plan steps.

## Implementation workflow (per task — mandatory)

Every `/speckit-implement` execution for a pending task follows these steps in order; none may
be skipped:

1. **Branch** — create and switch to `feature/<task-id>` (e.g. `feature/T002`) off `main`.
2. **Technical plan (tollgate)** — output the exact files to create/modify, the architectural
   approach (vertical slice, MediatR, standalone components…), and the testing strategy. Then
   **stop**: write no code until the user explicitly replies "Approved, proceed".
3. **Implement with TDD** — tests first, verified failing (Principle III), then implementation;
   validate locally (`dotnet build`, `dotnet test`, frontend build/tests).
4. **Commit & PR** — semantic commit, push the branch, open a PR against `main` (`gh` CLI).
5. **Automated review** — run the `senior-code-reviewer` agent
   (`.claude/agents/senior-code-reviewer.md`) on the PR diff and post its findings to the PR as
   an informational comment. The agent never approves/requests-changes its own session's PR
   (two-party review); a human decides on approval and merge.
6. **Documentation phase (mandatory before closing any task)** —
   (a) **ADR evaluation**: if the task involved a significant technical decision (framework
   change, design pattern, core library, DB schema), add a sequential ADR in `Docs/adrs/`
   (e.g. `0004-uso-de-redis-para-cache.md`, format per `Docs/adrs/template.md`);
   (b) **Living documentation**: update `Docs/arquitectura_viva.md` so it faithfully reflects
   the current system state — new endpoints, components, data flows. Both ship in the same
   change as the implementation (Principle X). **All documentation (ADRs, living doc, any new
   docs) is always written in English**; only the legacy product definition under `Docs/`
   remains in Spanish.

## Commands

Verified against the running system at Phase 1 close (T007); keep in sync with `quickstart.md`
(Principle X). Prerequisites: Docker Desktop running, .NET 10 SDK, Node.js 24+ (Jest loads
`jest.config.ts` via Node's native TS type stripping — no ts-node; verified on 24.18).

```bash
# Full local topology, one command (FR-044): PostgreSQL 16, Keycloak 26 (realm auto-import),
# Mailpit, API, Angular frontend. EF migrations + BJCP seed arrive with T009/T010.
dotnet run --project backend/src/BirraPoint.AppHost
# Aspire dashboard https://localhost:17202 (login URL printed on startup)
# API http://localhost:5121 · https://localhost:7075 — HTTP surface today: /health + /alive only
#   (Development-only; OpenAPI arrives with the first business endpoints)
# PWA http://localhost:4200 · Keycloak http://localhost:8081 (realm `birrapoint`)
# Mailpit UI: dynamic port — open it from the Aspire dashboard

dotnet build backend/BirraPoint.sln
dotnet test backend/tests/BirraPoint.Api.UnitTests            # handlers + validators
dotnet test backend/tests/BirraPoint.Api.IntegrationTests     # contract tests; needs Docker (Testcontainers)
dotnet test <project> --filter "FullyQualifiedName~SubmitEvaluation"   # single test
dotnet format backend/BirraPoint.sln --verify-no-changes      # backend format gate

cd frontend && npm ci && npm start     # frontend-only iteration (ng serve on :4200)
cd frontend && npx jest                # unit (jest-preset-angular)
cd frontend && npm run e2e             # Playwright E2E incl. e2e/a11y (axe) suite — config in e2e/,
                                       #   so plain `npx playwright test` does NOT work
cd frontend && npx ng lint             # angular-eslint
cd frontend && npm run format:check    # Prettier gate (`npm run format` to fix)

azd up   # cloud deploy — NOT yet real: azure.yaml + infra/bicep/ land in Phase 16
```

## Repository layout (per plan.md — binding)

```text
backend/src/BirraPoint.AppHost/         # Aspire orchestration (local topology + azd model)
backend/src/BirraPoint.ServiceDefaults/ # OpenTelemetry, health checks, resilience
backend/src/BirraPoint.Api/             # single deployable modular monolith
├── Domain/        # shared kernel: entities, enums, invariants (see data-model.md)
├── Common/        # cross-cutting: auth, persistence, errors, MediatR behaviors, audit, job queue
├── Features/      # ONE vertical slice per capability: Competitions, Catalog, Import, Judges,
│                  #   Tables, TastingOrder, Evaluations, Monitoring, Dispatch
└── Realtime/      # CompetitionHub + emit-after-commit event publisher
backend/tests/     # BirraPoint.Api.UnitTests + BirraPoint.Api.IntegrationTests
frontend/src/app/  # Feature-Sliced Design: core/ (auth, api, realtime, offline), features/, shared/
frontend/e2e/      # Playwright suites
infra/             # bicep/, backup/ (pg_dump job + RESTORE.md), keycloak/birrapoint-realm.json
azure.yaml         # azd project definition
```

Organize by business capability, never by technical layer (no `controllers/`, `services/`,
`repositories/` folders).

## Backend conventions

- A slice = request + handler + FluentValidation validator + endpoint mapping, together under
  `Features/<Capability>/`. Slices never touch another slice's internals — cross-slice interaction
  only via MediatR messages or shared contracts. Keep the shared kernel (`Domain/`, `Common/`)
  deliberately small.
- **MediatR stays pinned to 12.x** — 13+ moved to a commercial license (R-03). Validation runs in
  the MediatR pipeline (`ValidationBehavior`), before any handler logic.
- Errors: RFC 7807 ProblemDetails everywhere, with the stable `urn:birrapoint:*` type URNs from
  `contracts/rest-api.md` §Error catalog (14 entries — closed list; new ones require a contract
  amendment). `400` validation, `409` domain/state conflicts, `404` for resources outside the
  caller's scope (never reveal existence).
- SignalR: single `CompetitionHub`, **server → client only** — all mutations via REST. Groups
  `competition:{id}:organizers` (role + ownership guarded) and `table:{tableId}` (membership
  guarded). Events emit **after** the owning transaction commits and are notifications, not the
  source of truth (clients re-fetch on reconnect).
- Background work: DB-persisted `DispatchJob` queue + hosted `BackgroundService` (R-06 — no
  Hangfire, no broker). Jobs are idempotent and resume on startup.
- Judge provisioning goes through the Keycloak Admin REST API (temporary password + required
  action `UPDATE_PASSWORD`); invitation/result emails are sent app-side via MailKit for
  per-recipient status and retry (R-10).

## Frontend conventions

- Angular 20 standalone components + Signals, Feature-Sliced Design. Cross-cutting infrastructure
  in `core/`, business screens in `features/<capability>/`, primitives in `shared/`.
- Offline engine (R-08): Dexie stores `drafts` (persist ≤ 300 ms after each change) and `outbox`
  (submitted-but-unsynced). Replay on `window online`, app start, and after each submit; do NOT
  use the Background Sync API (unsupported on iOS Safari). IndexedDB is never the source of truth.
- Auth: `keycloak-angular` + `keycloak-js`, Authorization Code + PKCE; realm roles map to
  `/organizer/**` and `/judge/**` route guards. Forced password change happens inside the
  Keycloak-hosted flow — the app never sees tokens until required actions complete.

## Non-negotiable invariants

1. **Blind anonymity (BR-01/FR-019)** — judge-facing DTOs and SignalR payloads **physically lack
   entrant fields** (no beer name, participant, brewery, origin). Judges see blind code + style
   only. Contract tests assert field absence. This is a security invariant, not a UI rule.
2. **TDD (Principle III)** — tests written first, verified failing, then implementation. Test
   tasks precede implementation tasks in `tasks.md`; never reorder or skip them.
3. **Competition state machine (FR-006)** — `Draft → Active → InEvaluation → Finalized`,
   forward-only, skip-free, organizer-only; per-state capability gates in `data-model.md`.
4. **Idempotent sync (FR-029/R-07)** — unique index `(JudgeId, BeerEntryId)`; deterministic
   `X-Idempotency-Key: {competitionId}:{tableId}:{judgeId}:{entryId}`; replay returns `200` with
   the stored evaluation. Never UPSERT (would violate locked-on-submit).
5. **Immutability** — sheets lock on submit (reopened only via an open discrepancy involving that
   judge); after table close, judge mutations are rejected including late offline syncs (FR-034);
   only organizer corrections are allowed, always audit-logged (FR-035).
6. **Scoring rules** — section caps Aroma 12 / Appearance 3 / Flavor 20 / Mouthfeel 5 / Overall 10;
   total is computed (≤ 50), never editable; comments ≥ 20 chars per section; totals > 7 points
   apart on one sample → provisional + discrepancy alert, blocks table close (FR-031/FR-032).
   Evaluation requires: state `InEvaluation`, order fixed, strictly sequential samples (FR-022).
7. **Security (Principle VII)** — identity is Keycloak-only (no custom login/password/token code);
   deny-by-default `RequireAuthorization()` fallback + `ORGANIZER`/`JUDGE` role policies per
   endpoint; validate all input at the API boundary; EF Core parameterized queries only; secrets
   via environment variables, never in the repo; never log sensitive data.
8. **Accessibility (Principle VIII)** — WCAG 2.1 AA on all judge-facing flows; every drag & drop
   has a keyboard-accessible equivalent; axe-core Playwright checks are a merge gate.
9. **Performance budgets (Principle IX)** — API p95: reads < 200 ms, writes < 500 ms; realtime
   propagation (order fix, dashboard) < 1 s; draft save < 300 ms; initial JS bundle ≤ 500 KB
   gzipped; PWA interactive < 3 s on 4G.
10. **Contract-first (Principle VI)** — endpoints and hub events exist in `contracts/` before
    implementation; breaking a contract requires a spec amendment and versioning decision.

## Approved stack (pinned — constitution + research)

- **Backend**: .NET 10 LTS / C# 14, ASP.NET Core Minimal APIs, MediatR 12.5.x, FluentValidation,
  EF Core + Npgsql (PostgreSQL 16, code-first migrations), SignalR, ClosedXML (xlsx), QuestPDF
  Community (PDF), MailKit (SMTP), .NET Aspire AppHost + ServiceDefaults.
- **Frontend**: Angular 20 (standalone + Signals), `@angular/pwa`, Dexie.js, Tailwind CSS,
  `@angular/cdk/drag-drop`, `keycloak-angular`/`keycloak-js`, `@microsoft/signalr`.
- **Testing**: xUnit; `WebApplicationFactory` + Testcontainers (real PostgreSQL — no EF InMemory);
  Jest via `jest-preset-angular` (not Karma); Playwright + `@axe-core/playwright`.
- **Identity/Infra**: Keycloak 25+ (OIDC, roles `ORGANIZER`/`JUDGE`); multi-stage Docker images
  (no baked secrets); Bicep + `azd` → Azure Container Apps; Mailpit locally.

Any dependency beyond this list must be justified in the plan (Principle V); micro-dependencies
are rejected.

## Definition of Done (every task/story)

- Tests written first and passing: unit, integration/contract, and the story's E2E scenario from
  `quickstart.md`; lint/format and build green for both stacks.
- Accessibility checks pass; performance budget respected.
- Documentation updated in the same change (Principle X): contracts for endpoint changes,
  `quickstart.md` for setup changes, `Docs/` for product-behavior changes, this file when
  commands or structure change; ADR in `Docs/adrs/` when a significant technical decision was
  made, and `Docs/arquitectura_viva.md` refreshed to the current system state (workflow step 6).
- If a check cannot run, say why and what was verified instead.

## Git

- Per-task branches `feature/<task-id>` off `main`; PRs target `main` (see Implementation
  workflow); small, reviewable commits.
- Commit spec artifacts together with the implementation they describe.
- Never force-push or skip hooks (`--no-verify`).
