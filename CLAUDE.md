# Project Instructions

## Project

BirraPoint: PWA for beer competitions with blind tastings. Organizer: competition wizard (drafts,
`.xlsx` entry import validated against BJCP 2021), judge invitations, tasting tables with
conflict-of-interest protection, real-time dashboard, immutable closing with PDF/ZIP/email
dispatch. Judge: offline-first evaluation, shared fixed tasting order, BJCP score caps,
discrepancy consensus. Out of scope: Best of Show, tie-breaks (only `NotValidForBos` flag).

Status: US1–US14 done. Pending work lives in `specs/001-birrapoint-mvp/tasks.md` (T092 usability
study; Phase 16 deploy/ops: T098, T099, T129–T132, T137, T139). Current system state: `Docs/arquitectura_viva.md`.
`Docs/` product definition is Spanish and superseded by the English spec.

## Source of truth (priority order)

1. `.specify/memory/constitution.md` (v1.5.1) — overrides everything; stack changes need an amendment.
2. `specs/001-birrapoint-mvp/`: `spec.md` (US/FR/SC) → `plan.md` → `tasks.md`; supporting
   `research.md` (R-01–R-21), `data-model.md`, `contracts/` (`rest-api.md`, `signalr-hub.md`,
   `import-file.md`), `quickstart.md` (one validation scenario per story).

No implementation without approved spec + plan + task. Requirement changes go into the spec
first, never silently into code.

## Workflow

- Spec Kit order: constitution → specify → clarify → plan → tasks → review → implement. Helper
  scripts are PowerShell only (`.specify/scripts/powershell/`); `.specify/feature.json` points at
  the active feature.
- **Per task (`/speckit-implement`, mandatory, in order):**
  1. Branch `feature/<task-id>` off `main`.
  2. Tollgate: list files to create/modify, approach, test strategy. **Stop** until the user
     replies "Approved, proceed".
  3. TDD: tests first, verified failing, then code; build + test both stacks locally.
  4. Semantic commit, push, PR to `main` via `gh`.
  5. `/review-pr <n>`: runs `senior-code-reviewer` and posts its findings as an informational PR
     comment (never approve/request changes — a human merges).
  6. Docs in the same change (`docs-keeper` agent): ADR in `Docs/adrs/NNNN-kebab-title.md` (per
     `template.md`) for significant decisions; update `Docs/arquitectura_viva.md`. All docs in
     English.
- Delegate work to subagents (`.claude/agents/`); orchestration (branch/tollgate/PR) stays in
  the main session:

  | Agent | Owns |
  |---|---|
  | `backend-engineer` | `backend/src/**` + unit tests |
  | `frontend-engineer` | `frontend/src/**` + Jest, visual check in Chrome |
  | `qa-engineer` | integration/contract tests, `frontend/e2e`, `infra/perf` |
  | `infra-engineer` | `infra/**`, `.github/**`, Dockerfiles (never applies to Azure) |
  | `docs-keeper` | step 6: living doc, ADRs, doc drift |
  | `senior-code-reviewer` | step 5 review (read-only) |

- Commands: `/review-pr <n>`, `/review-and-commit`, `/fix-tests [backend|frontend]`,
  `/offline-audit`. Hooks (`.claude/hooks/`): `guard.js` blocks destructive/cloud commands,
  `post-edit.js` formats edited files, `stop-check.js` builds/lints/type-checks changed stacks.
- CodeGraph (`.codegraph/`, local-only): call `codegraph_explore` when you need to locate code or
  see callers, before grep/read loops.

## Commands

Prereqs: Docker Desktop, .NET 10 SDK, Node 24+.

```bash
dotnet run --project backend/src/BirraPoint.AppHost   # full local stack: Postgres 16, Keycloak 26,
  # Mailpit, API, PWA. Aspire https://localhost:17202 · API :5121/:7075 · PWA :4200 ·
  # Keycloak :8081 (realm birrapoint) · Mailpit port via Aspire dashboard
dotnet build backend/BirraPoint.sln
dotnet test backend/tests/BirraPoint.Api.UnitTests
dotnet test backend/tests/BirraPoint.Api.IntegrationTests   # Testcontainers, needs Docker
dotnet test <project> --filter "FullyQualifiedName~Name"
dotnet format backend/BirraPoint.sln --verify-no-changes

cd frontend && npm ci && npm start    # ng serve :4200
npx jest | npx ng lint | npm run format:check | npm run build:budget   # (from frontend/)
npm run e2e                           # Playwright + axe; plain `npx playwright test` fails
k6 run infra/perf/api-budgets.js      # needs bearer token, see script header

# Azure (one cloud per deployment, ADR-0023)
terraform -chdir=infra/azure/terraform init   # state in HCP Terraform: TF_CLOUD_ORGANIZATION, TF_WORKSPACE, terraform login
terraform -chdir=infra/azure/terraform apply -var-file=environments/prod.tfvars -var release_version=X.Y.Z   # ADR-0022
terraform -chdir=infra/azure/terraform test   # mocked providers, no credentials
terraform -chdir=infra/aws/terraform init && terraform -chdir=infra/aws/terraform apply -var-file=environments/prod.tfvars -var release_version=X.Y.Z   # AWS, local apply (see its README)
terraform -chdir=infra/aws/terraform test   # mocked providers, no credentials
./infra/azure/teardown.ps1 [-Environment X] [-WhatIf] [-Force]   # wipes everything incl. Neon data
./infra/aws/teardown.ps1 [-Environment X] [-WhatIf] [-Force]     # same for AWS
gh workflow run release.yml -f ref=main -f bump=patch   # ADR-0019
gh workflow run deploy-azure.yml -f version=X.Y.Z       # terraform apply; see infra/azure/github-actions-setup.md
gh workflow run deploy-aws.yml -f version=X.Y.Z         # AWS apply; see infra/aws/github-actions-setup.md
bash .github/scripts/version.test.sh
bash .github/scripts/wait-revisions.test.sh
bash .github/scripts/ecs-health.test.sh
node .claude/hooks/guard.test.js
bash infra/keycloak/dapr-secrets/DaprSecretsEnv.test.sh
Invoke-Pester infra/azure/tests       # Pester 5+
Invoke-Pester infra/aws/tests
docker build -f backend/src/BirraPoint.Api/Dockerfile backend | docker build frontend | docker build infra/keycloak
```

## Layout (binding — by business capability, never `controllers/`/`services/`/`repositories/`)

```text
backend/src/BirraPoint.AppHost | ServiceDefaults | Api/
  Api/Domain/    shared kernel (keep small)     Api/Common/   auth, persistence, errors, behaviors, audit, job queue
  Api/Features/  one slice per capability: Competitions, Catalog, Import, Judges, Tables,
                 TastingOrder, Evaluations, Monitoring, Dispatch
  Api/Realtime/  CompetitionHub + emit-after-commit publisher
backend/tests/   UnitTests + IntegrationTests
frontend/src/app/  FSD: core/ (auth, api, realtime, offline, layout), features/, shared/
frontend/e2e/    Playwright (+ e2e/a11y axe)
infra/           azure/ and aws/, each: terraform/ (azure: ACA, Key Vault, Dapr, Neon; aws: ECS Fargate,
                 ALB, CloudFront, Secrets Manager), teardown.ps1 + Teardown.psm1, tests/ (Pester),
                 github-actions-setup.md + deployment-runbook.md; ci-setup.md (shared CI setup);
                 keycloak/ (realm, theme, Dockerfile, dapr-secrets), perf/ (k6)
```

## Backend conventions

- Slice = request + handler + FluentValidation validator + endpoint under `Features/<X>/`.
  Cross-slice only via MediatR messages or shared contracts. **MediatR pinned 12.x** (licensing).
- Errors: RFC 7807 with `urn:birrapoint:*` types from `rest-api.md` error catalog (closed list).
  `400` validation, `409` state conflict, `404` for out-of-scope resources (never reveal existence).
- SignalR: one `CompetitionHub`, server→client only; groups `competition:{id}:organizers`,
  `table:{tableId}`; emit after commit; clients re-fetch on reconnect.
- Background: DB `DispatchJob` queue + `BackgroundService`, idempotent, resumes on startup.
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

`feature/<task-id>` branches, PRs to `main`, small commits, spec artifacts committed with their
implementation. Never force-push or `--no-verify`.
