# Project Instructions

## Project

BirraPoint: PWA for beer competitions with blind tastings. Organizer: competition wizard (drafts,
`.xlsx` entry import validated against BJCP 2021), judge invitations, tasting tables with
conflict-of-interest protection, real-time dashboard, immutable closing with PDF/ZIP/email
dispatch. Judge: offline-first evaluation, shared fixed tasting order, BJCP score caps,
discrepancy consensus. Out of scope: Best of Show, tie-breaks (only `NotValidForBos` flag).

Status: MVP complete (US1–US14). New work = GitHub issues on project board
https://github.com/users/jesuscorral/projects/10, each specified in its own `specs/NNN-<slug>/`.
Current system state and known debt: `Docs/arquitectura_viva.md`.

## Source of truth (priority order)

1. `.specify/memory/constitution.md` (v1.6.0) — overrides everything; stack changes need an amendment.
2. The feature's `specs/NNN-<slug>/` (`NNN` = issue number): `spec.md` → `plan.md` → `tasks.md`.
3. Baseline `specs/001-birrapoint-mvp/`: `spec.md` (US1–US14, FR-001–FR-065, SC-001–SC-013),
   `research.md` (R-01–R-21), `quickstart.md` (one validation scenario per story) and the
   **living contracts** every feature amends in place: `data-model.md`, `contracts/`
   (`rest-api.md`, `signalr-hub.md`, `import-file.md`, `judge-import-file.md`).

No implementation without approved spec + plan + tasks. Requirement changes go into the spec
first, never silently into code. New `FR-`/`SC-`/`US` ids continue the global sequence.

## Workflow

- **Per feature (`/deliver <issue>` drives it; mandatory, in order, ADR-0040):**
  1. Issue exists (template `.github/ISSUE_TEMPLATE/feature.yml`: What and why, Done when, notes;
     auto-added to the project board).
  2. Branch `feature/<issue>-<slug>` off `main`.
  3. Spec Kit in `specs/NNN-<slug>/`: specify (from the issue) → clarify → **Stop 1: user approves
     the spec** → plan → tasks → analyze. Skills in `.claude/skills/speckit-*`; helper scripts
     PowerShell only (`.specify/scripts/powershell/`); `.specify/feature.json` = active feature.
  4. Tollgate: files to create/modify, approach, test strategy, owning agents. **Stop 2** until the
     user replies "Approved, proceed".
  5. TDD: tests first, verified failing, then code; build + test both stacks locally.
  6. Semantic commit, push, PR to `main` via `gh`: title `<type>(<scope>): <subject> (#<issue>)`,
     body from `.github/pull_request_template.md` with `Closes #<issue>` (`pr.yml` checks title +
     "What and why" and labels the PR; ADR-0036).
  7. `/review-pr <n>`: `senior-code-reviewer` findings as an informational PR comment plus inline
     comments (never approve/request changes — a human merges). `/address-review <n>` fixes them.
  8. Docs in the same change (`docs-keeper`): ADR in `Docs/adrs/NNNN-kebab-title.md` (per
     `template.md`) for significant decisions; update `Docs/arquitectura_viva.md`. All docs in
     English.
- Board status (project 10) follows the flow, set with `bash .claude/scripts/board-status.sh <issue>
  "<status>"`: **Backlog** on creation (automatic) → **Ready** once the spec is approved (Stop 1) →
  **In progress** when implementation starts (after Stop 2) → **In review** when the PR is opened →
  **Done** when the PR is merged (automatic, board workflow).
- Small fixes/chores without new behaviour: `fix/<topic>` or `chore/<topic>` branch,
  `/review-and-commit`, PR; no spec folder needed.
- Delegate work to subagents (`.claude/agents/`: backend-, frontend-, qa-, infra-engineer,
  docs-keeper, senior-code-reviewer — each states its scope); orchestration (branch/tollgate/PR)
  stays in the main session. No agent ever applies to a cloud account.
- Commands: `/deliver <issue>`, `/review-pr <n>`, `/address-review <n>`, `/review-and-commit`,
  `/fix-tests [backend|frontend]`, `/offline-audit`, `/stack-status`; skill `migration-safety` for
  EF migrations. Hooks (`.claude/hooks/`): `session-start.js` prints branch, active spec folder + open issues,
  `guard.js` blocks destructive/cloud/secret-reading commands, PR merges/approvals and commits or
  pushes on `main`; `post-edit.js` formats edited files; `stop-check.js` builds/lints/type-checks
  changed stacks.
- MCP: `postgres-local` (read-only, `.claude/mcp/postgres-local.js`) needs the Aspire stack running.
  `aspire` (`aspire agent mcp`, local backchannel only, ADR-0039): agents read resources/logs/traces; `execute_resource_command` prompts, main session only. Needs Aspire CLI ≥ 13.6.4.

## Commands

Prereqs: Docker Desktop, .NET 10 SDK, Node 24+.

```bash
dotnet run --project src/Aspire/BirraPoint.AppHost   # full local stack: Postgres 16, Keycloak 26,
  # Mailpit, API, PWA. Aspire https://localhost:17202 · API :5121/:7075 · PWA :4200 ·
  # Keycloak :8081 (realm birrapoint) · Mailpit port via Aspire dashboard
# Reset local DB (drops only `birrapoint`, Keycloak kept; ADR-0041): Aspire dashboard -> `db` -> Reset database
dotnet build BirraPoint.sln
dotnet test tests/BirraPoint.Api.UnitTests
dotnet test tests/BirraPoint.Api.IntegrationTests   # Testcontainers, needs Docker
dotnet test <project> --filter "FullyQualifiedName~Name"
dotnet format BirraPoint.sln --verify-no-changes

cd src/Birrapoint.Web && npm ci && npm start    # ng serve :4200
npx jest | npx ng lint | npm run format:check | npm run build:budget   # (from src/Birrapoint.Web/)
npm run e2e                           # Playwright + axe; plain `npx playwright test` fails
k6 run infra/perf/api-budgets.js      # needs bearer token, see script header

# Cloud (one per deployment, ADR-0023): Terraform roots infra/azure|aws/terraform, state in HCP
# Terraform; apply/teardown only by a human — see infra/<cloud>/deployment-runbook.md
gh workflow run release.yml -f ref=main -f bump=patch   # ADR-0019
gh workflow run deploy-azure.yml -f version=X.Y.Z       # or deploy-aws.yml
# Infra/CI test gates (terraform test, Pester, .github/scripts/*.test.sh, Keycloak scripts):
# listed in .claude/agents/infra-engineer.md. Harness: node .claude/hooks/guard.test.js
```

## Layout (binding — by business capability, never `controllers/`/`services/`/`repositories/`)

```text
src/Aspire/{BirraPoint.AppHost, BirraPoint.ServiceDefaults} | src/BirraPoint.Api/
  Api/Domain/    shared kernel: Entity(+ITimestamped), DispatchJobType names     Api/Common/   AppDbContext, auth policies, persistence, host errors (validation, rate limit)
  Api/Composition/ host wiring between blocks (audit actor)
  Api/Modules/   ADR-0033/0034: Catalog (Catalog), Organization (Competitions, Import, Judges), Judging
                 (Tables, TastingOrder, Evaluations, Monitoring), Publication (Dispatch); each module =
                 Domain/ + Errors/ (if it throws, ADR-0038) + Persistence/ + Contracts/ + Integration/ + <M>Module.cs + <M>ModuleEndpoints.cs +
                 Features/<Feature>/ (Base<X>Endpoints.cs + Features/<Slice>/ + CommonComponents/, ADR-0031).
                 Other modules only via Contracts/ (ModuleBoundaryTests)
  Api/Realtime/  CompetitionHub + emit-after-commit publisher
src/BuildingBlocks/BuildingBlocks.<X>/   ADR-0035/0037: reusable cross-cutting blocks (Audit, DaprSecrets, EmailSender, Identity, Keycloak.Authentication, Keycloak.Admin, ProblemDetails, Validation.MediatR, Spreadsheet, Jobs, Messaging), no BirraPoint
                 reference, public API in BuildingBlocks.<X>; tests in tests/BuildingBlocks/
tests/   UnitTests + IntegrationTests
src/Birrapoint.Web/src/app/  FSD: core/ (auth, api, realtime, offline, layout), features/, shared/
src/Birrapoint.Web/e2e/    Playwright (+ e2e/a11y axe)
infra/           azure/ and aws/, each: terraform/ (azure: ACA, Key Vault, Dapr, Neon; aws: ECS Fargate,
                 ALB, CloudFront, Secrets Manager), teardown.ps1 + Teardown.psm1, tests/ (Pester),
                 github-actions-setup.md + deployment-runbook.md; ci-setup.md (shared CI setup);
                 keycloak/ (realm, theme, Dockerfile, dapr-secrets), perf/ (k6)
```

## Backend conventions

- Slice = request + handler + FluentValidation validator + endpoint under `Modules/<M>/Features/<X>/Features/<Slice>/`
  (shared types in `CommonComponents/`; namespaces = folders, ADR-0031).
  In-module requests use MediatR; cross-module writes only via integration events (`Contracts/IntegrationEvents/`, `BuildingBlocks.Messaging` outbox/inbox, ADR-0042; at-least-once, handlers idempotent and order-insensitive) or `Contracts/` interfaces. **MediatR pinned 12.x** (licensing).
- Errors: RFC 7807 with `urn:birrapoint:*` types from `rest-api.md` error catalog (closed list); each module declares its own errors in `Modules/<M>/Errors/`, the host only validation + rate limit (ADR-0038).
  `400` validation, `409` state conflict, `404` for out-of-scope resources (never reveal existence).
- Moving/renaming C# files: `git mv`, then `node .claude/scripts/move-namespace.mjs` (fixes namespaces +
  usings, ~1 s; `--dry-run` to preview). Never write an ad hoc script for it. If namespaces were edited
  by hand: `dotnet build ... 2>&1 | node .claude/scripts/move-namespace.mjs --fix-build`. `IDE0130`
  (namespace = folder) is a build error; `dotnet build` stays the final check.
- SignalR: one `CompetitionHub`, server→client only; groups `competition:{id}:organizers`,
  `table:{tableId}`; emit after commit; clients re-fetch on reconnect.
- Background: DB job queue (`BuildingBlocks.Jobs`, `DispatchJobs` table) + `BackgroundService`, idempotent, atomic lease-based claim (`SKIP LOCKED`), recovers expired leases.
- Judges provisioned via Keycloak Admin API (temp password + `UPDATE_PASSWORD`); emails via MailKit.

## Frontend conventions

- Angular 20 standalone + Signals. Organizer screens desktop-first; judge flows mobile/tablet.
- Offline (R-08): Dexie `drafts` (save ≤ 300 ms) + `outbox`; replay on `online`, app start, after
  submit. No Background Sync API. IndexedDB never source of truth.
- Auth: `keycloak-angular` + PKCE; roles guard `/organizer/**`, `/judge/**`.

## Non-negotiable invariants

1. **Blind anonymity**: judge DTOs/SignalR payloads physically lack entrant fields (name,
   participant, brewery, origin); contract tests assert absence.
2. **TDD**: tests first and failing; never reorder/skip test tasks.
3. **State machine**: `Draft → Active → InEvaluation → Finalized`, forward-only, no skips,
   organizer-only; capability gates in `data-model.md`.
4. **Idempotent sync**: unique `(JudgeId, BeerEntryId)`; `X-Idempotency-Key:
   {competitionId}:{tableId}:{judgeId}:{entryId}`; replay → `200` stored evaluation. Never UPSERT.
5. **Immutability**: sheet locks on submit (reopen only via open discrepancy for that judge);
   after table close, judge mutations rejected incl. late offline sync; organizer corrections
   always audit-logged.
6. **Scoring**: caps Aroma 12 / Appearance 3 / Flavor 20 / Mouthfeel 5 / Overall 10; total
   computed (≤ 50); comments ≥ 20 chars/section; totals > 7 apart → discrepancy, blocks table
   close. Evaluating requires `InEvaluation`, fixed order, strictly sequential samples.
7. **Security**: Keycloak-only identity; deny-by-default authorization + `ORGANIZER`/`JUDGE`
   policies; validate at API boundary; parameterized EF only; secrets via env locally, Key Vault
   (Azure) or Secrets Manager (AWS) + Dapr in the cloud (ADR-0021/0023); never log sensitive data.
8. **Accessibility**: WCAG 2.1 AA on judge flows; keyboard alternative for every drag & drop;
   axe checks gate merges.
9. **Performance**: API p95 reads < 200 ms, writes < 500 ms; realtime < 1 s; initial JS ≤ 500 KB
   gzip; PWA interactive < 3 s on 4G.
10. **Contract-first**: endpoints/events in `contracts/` before code; breaking changes need spec
    amendment + versioning.

## Stack (pinned; extra deps need plan justification, no micro-deps)

- Backend: .NET 10 / C# 14, Minimal APIs, MediatR 12.5.x, FluentValidation, EF Core + Npgsql
  (Postgres 16, code-first migrations), SignalR, ClosedXML, QuestPDF Community, MailKit, Aspire,
  `Dapr.Extensions.Configuration` (prod secrets only).
- Frontend: Angular 20, `@angular/pwa`, Dexie, Tailwind, `@angular/cdk/drag-drop`,
  `keycloak-angular`/`keycloak-js`, `@microsoft/signalr`.
- Tests: xUnit, `WebApplicationFactory` + Testcontainers (no EF InMemory), Jest
  (`jest-preset-angular`, not Karma), Playwright + `@axe-core/playwright`.
- Infra: Keycloak, multi-stage Docker (no baked secrets), Terraform → Azure Container Apps
  (managed identities, Dapr) + Key Vault, or AWS ECS Fargate + ALB + CloudFront + Secrets Manager
  (Dapr) — one per deployment, separate roots; Neon Postgres, Mailpit locally.

## Definition of Done

Tests first and green (unit, integration/contract, story E2E from `quickstart.md`); lint/format/
build green both stacks; a11y + perf budgets respected; docs updated in the same change
(contracts, `quickstart.md`, this file if commands/structure change, ADR, living doc). If a check
can't run, say why and what was verified instead.

## Git

`feature/<issue>-<slug>` (or `fix/`/`chore/<topic>`) branches, PRs to `main`, small commits, spec artifacts committed with their
implementation. Never force-push or `--no-verify`.

Issue numbers and task ids (like `T` plus three digits, from a feature's `tasks.md`) live in branch
names, commit messages, PR titles and `specs/` only — never in code
comments, test names or config under `src/` (PWA included), `tests/`, `infra/`, `.github/` (enforced by
`.github/scripts/check-no-task-ids.sh`, run by CI and the stop hook). In code, cite the requirement
(`FR-`/`SC-`/`US`/ADR) or state the reason.
