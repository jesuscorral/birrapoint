# Living Architecture — BirraPoint

> The **current** state of the system. Update it at the close of every task (CLAUDE.md, workflow
> step 6). It describes what exists, not how it got there: history lives in git and PRs, decisions
> with trade-offs in `Docs/adrs/`, the approved design in `specs/001-birrapoint-mvp/`.

**Last updated:** 2026-10-04

## Status

- **Functional scope complete**: user stories US1–US14 are implemented, each with unit,
  contract/integration and E2E coverage (`quickstart.md` scenarios).
- **Open work** (`tasks.md`): T092 (SC-010 human usability study), T098 (health/telemetry in ACA),
  T099 (first validated cloud deploy), T129–T132 (PR #46 follow-ups), T137 (CI/CD validation
  against a real deployment), T139 (E2E + a11y job in CI), T146 (AWS deploy workflow + teardown).
- **Two cloud targets, one per deployment** (ADR-0023): Azure Container Apps is implemented under
  `infra/azure/`; the AWS Terraform root exists under `infra/aws/terraform/` (not yet applied to a real account); its deploy workflow and teardown arrive with T146 (deploy meanwhile is a local `terraform apply`, see its README).
- **Not yet deployed to a real Azure subscription.** The whole Azure path (Terraform,
  `deploy-azure.yml`, `infra/azure/teardown.ps1`) is implemented and unit-tested but unvalidated
  end to end (T099/T137).

## Topology

### Local (.NET Aspire — `dotnet run --project backend/src/BirraPoint.AppHost`)

| Resource | Implementation | Endpoint | Notes |
|---|---|---|---|
| `postgres` (`db`, `keycloakdb`) | `postgres:16`, persistent volume | dynamic | `db` is the app database; `keycloakdb` is Keycloak's store (ADR-0013) |
| `keycloak` | `quay.io/keycloak/keycloak:26.2` via `AddContainer` (ADR-0001) | :8081 | realm `birrapoint` imported from `infra/keycloak/` (`IGNORE_EXISTING`); login theme bind-mounted; seeded `organizer`/`organizer` account |
| `mailpit` | CommunityToolkit integration | UI :8025, SMTP dynamic | invitation, result and Keycloak password-reset emails |
| `api` | `BirraPoint.Api` | :5121 / :7075 | env: `Keycloak__*`, `Smtp__*`, `Frontend__BaseUrl`; migrates on startup in Development |
| `frontend` | `npm start` via `AddJavaScriptApp` | :4200 | serves `public/config.json` |

**Troubleshooting.** "Can't log in / data gone" is almost always a Keycloak credential problem,
not data loss: check the `db` database directly first. A recreated Keycloak user gets a new `sub`;
rows keyed by the old one (`Organizers.KeycloakUserId`, `Competitions.CreatedByUserId`,
`Judges.KeycloakUserId`) must be re-pointed by hand. Dropping the `keycloak` database forces a
realm re-import and deletes every user created since the seed.

### Cloud (Terraform → Azure Container Apps + Neon, `infra/azure/`)

A deployment targets Azure **or** AWS, never both; each target is its own Terraform root, HCP
workspace, teardown and workflow, sharing only the Docker Hub images, `infra/keycloak` and the Neon
account (ADR-0023).

AWS (`infra/aws/terraform/`, Terraform only; deploy workflow and teardown arrive with T146):

- Two CloudFront distributions (web, Keycloak) → one ALB (admits only the CloudFront prefix list plus a secret origin header; routes web vs Keycloak by that header) → ECS Fargate web / Keycloak.
- API is internal (no target group), exactly 1 replica, reached by the web through a Cloud Map private DNS namespace (`api.birrapoint-<env>.local`).
- Secrets Manager read through Dapr sidecars on API and Keycloak only; Neon for Postgres; CloudWatch logs; dedicated VPC, public subnets, no NAT. One environment per AWS account and region.
- Open risks for the first apply: security-group rule quota with the CloudFront prefix list; Keycloak honoring the `Forwarded` header; API sees forwarded proto `http`; daprd on Fargate (loopback only); Cloud Map registration; Docker Hub pull limits. Each API deploy stops the old task first (short outage).
- Rationale and refinements: research R-21, ADR-0023.

Azure: every resource is named `birrapoint-<env>-<acronym>` (default env `PROD`, lower-cased;
ADR-0021).

| Resource | Implementation | Ingress | Notes |
|---|---|---|---|
| `-rg`, `-log`, `-cae` | resource group, Log Analytics (30 d), ACA environment | — | console logs → Log Analytics; OTLP export pending (T098) |
| `-kv` | Key Vault (RBAC, soft-delete 7 d, purged on destroy) + Dapr component `secretstore` | public, RBAC-only | all app secrets; `Key Vault Secrets User` granted **per secret** to the app that uses it |
| `-web` | `birrapoint-web`: Angular build → `nginx-unprivileged` | external | writes `/config.json` from env at start; reverse-proxies `/api/` and `/hubs/` (WebSocket) to the API (ADR-0017); security headers; hashed assets cached 1 year |
| `-api` | `birrapoint-api`: `aspnet:10.0`, non-root | **internal** | exactly 1 replica (SignalR without backplane, single job consumer); Dapr sidecar loads `ConnectionStrings:db` (Neon pooled), `ConnectionStrings:dbDirect` (direct, migrations only), `Keycloak:AdminClientSecret`, `Smtp:Password` |
| `-kc` | `birrapoint-keycloak`: optimized build + theme + production realm + Dapr secret loader | external | entrypoint loads its 5 secrets from the sidecar (≤180 s retry), then `start --optimized --import-realm`; probes on port 9000 |
| `-neon` | Neon project (`kislerdm/neon` provider), PG 16 | — | databases `birrapoint` and `keycloak` on one branch, so a PITR restores both consistently |

**Secrets** never enter an image or the repo. Neon passwords come from the provider; the Keycloak
bootstrap admin and API admin-client secrets from `random_password`; SMTP and the
optional Docker Hub token from `terraform.tfvars` (gitignored). Everything except the Docker Hub
pull token (a Container Apps registry secret) is written to Key Vault and read at startup through
Dapr. The production realm is derived at image build time from the local one: the seeded account
and local-dev secret fallbacks are stripped (the build fails if they survive).

**Deploy** (ADR-0022): Terraform only. `terraform init` + `terraform apply -var image_namespace=<ns>
[-var release_version=X.Y.Z] [-var api_version=…]` from a workstation (`az login`, `terraform login`,
`NEON_API_KEY`) or from `deploy-azure.yml`. Images are plain variables: `latest` →
`<ns>/birrapoint-<c>:latest`, `X.Y.Z` → `<ns>/birrapoint-<c>-release:X.Y.Z`; a changed version
creates a new revision (rollback = apply the previous version). `latest` on an existing
environment does not re-pull; update with release versions. State is in HCP Terraform (`cloud {}`;
`TF_CLOUD_ORGANIZATION`, `TF_WORKSPACE`; workspace in Local execution mode, so plans run with the
caller's Azure identity). Every deployment starts from scratch: the realm is imported with the
right `SPA_URL`, there is no post-deploy Keycloak sync.

**Teardown** (`infra/azure/teardown.ps1 [-WhatIf] [-Force]`): always wipes everything — `terraform destroy`
including the Neon project, sweep by name, Key Vault and Log Analytics purge, local `.terraform`.
No data is kept. Idempotent, with a direct sweep fallback when the destroy fails.

**Pipelines** (`.github/workflows/`):

| Workflow | Trigger | Does |
|---|---|---|
| `ci.yml` | PR, push to `main` | backend/frontend gates (`quality-gates.yml`, reusable), image builds; on `main` pushes `sha-<short>` and `latest` |
| `infra.yml` | `infra/**` changes | `terraform fmt`/`validate`/`test` (mocked providers), Pester (teardown), Dapr loader test |
| `workflows.yml` | `.github/**` changes | actionlint, `version.test.sh` |
| `release.yml` | manual, from `main` (ADR-0019) | builds `ref` (`main`, `hotfix/*`, `v*` tag), runs gates, pushes `-release:X.Y.Z` images, tags `vX.Y.Z`, creates the GitHub Release, bumps `version.txt` on `main`; idempotent re-runs |
| `deploy-azure.yml` | manual, from `main` | validates versions, then (after `azure-production` environment approval) OIDC runs `terraform init` + `apply` with the release versions; identity needs Contributor + User Access Administrator |

One-time setup: `infra/github-actions-setup.md`. Prerequisites and Neon PITR restore:
`infra/azure/terraform/README.md`.

## Backend (`backend/`, .NET 10 / C# 14)

Projects: `BirraPoint.Api` (modular monolith), `BirraPoint.AppHost`, `BirraPoint.ServiceDefaults`
(OpenTelemetry, health checks, HTTP resilience), `BirraPoint.Api.UnitTests`,
`BirraPoint.Api.IntegrationTests`. Package versions are centralized in
`backend/Directory.Packages.props` (including `$(BirraPointTargetFramework)`).

### Pipeline and cross-cutting (`Common/`)

- **Startup**: `AddDaprSecrets()` first (only when `Dapr:SecretStore` is set; retries while the
  sidecar or a new role assignment is not ready, then fails startup). Migrations run on startup in
  Development or when `Database:MigrateOnStartup=true`, over `dbDirect` when present. Outside
  Development: `UseForwardedHeaders`, no HTTPS redirection (TLS ends at ACA), HSTS on.
- **Middleware order**: exception handler → CORS (Development only, `localhost:4200`) →
  authentication → authorization → endpoints. Production is same-origin, no CORS.
- **Auth** (`Common/Auth/`): JWT bearer against Keycloak, audience `birrapoint-api` validated
  (ADR-0009); `KeycloakRolesClaimsTransformation` maps `realm_access.roles` to role claims;
  deny-by-default fallback policy + `ORGANIZER`/`JUDGE` policies. The hub reads the token from
  `?access_token=` on `/hubs/competition` only (ADR-0006). `ICurrentUser` exposes
  sub/email/name/roles; `OrganizerResolver` creates the `Organizer` row lazily on first write;
  `JudgeResolver` backfills `Judge.KeycloakUserId` on every competition row sharing the email.
  Ownership checks use `Competition.CreatedByUserId == sub`; misses return `404`.
- **Errors** (`Common/Errors/`): `DomainExceptionHandler` (14-entry `DomainErrorType` catalog →
  `urn:birrapoint:*`), `ValidationExceptionHandler` (`400` + field map), fallback `500` without
  internals.
- **Validation** (`Common/Behaviors/`): MediatR 12.5 + `ValidationBehavior` running every
  FluentValidation validator before the handler.
- **Audit** (`Common/Audit/`): `IAuditWriter.Record(...)` stages an `AuditLog` row (`{before,
  after}` jsonb) in the caller's transaction.
- **Persistence** (`Common/Persistence/`): `AppDbContext`, one configuration per entity, central
  `CreatedAt`/`UpdatedAt` stamping, enums stored as strings (ADR-0004). Key constraints: unique
  `(JudgeId, BeerEntryId)` on `Evaluation`; `Evaluation.Total` is a stored computed column; unique
  `BeerEntryId` on `TableSample`; unique `(TastingTableId, SequenceOrder)`; partial unique index on
  open `DiscrepancyAlert`s; date-window check constraints on `Competition`. The BJCP 2021 catalog
  (125 styles, full guide text) is seeded by migration from the embedded
  `Features/Catalog/Data/bjcp-2021.json`, with its SHA-256 pinned by a unit test (ADR-0005).
- **Background jobs** (`Common/Jobs/`, R-06): `DispatchJobQueue` inserts a `Pending` row and wakes
  `DispatchWorker` through a channel (plus a 30 s safety-net poll). The worker dispatches by
  `DispatchJobType` to an `IDispatchJobHandler`, retries with capped exponential backoff enforced
  by `NextAttemptAt` (ADR-0008, max 5 attempts), treats `Running` rows on startup as crashed
  attempts, and emits `DispatchProgress`. Handlers: `ProvisionJudgeAccount`, `SendInvitation`,
  `GeneratePdfs`, `BundleZip`, `SendResultEmail`.
- **Keycloak Admin** (`Common/Keycloak/`): client-credentials as `birrapoint-api-admin`.
  `EnsureUserWithTemporaryPasswordAsync` finds or creates the user, always grants `JUDGE`, and
  resets a temporary password (`UPDATE_PASSWORD` required action) only for accounts that never
  completed first login; returns `null` otherwise.
- **Email** (`Common/Email/`): MailKit `IEmailSender` (`SendAsync`, `SendWithAttachmentsAsync`);
  optional SMTP auth/STARTTLS.
- **Realtime** (`Realtime/`): `CompetitionHub` at `/hubs/competition`, server → client only.
  `JoinCompetitionAsOrganizer` (role + ownership) and `JoinTable` (active `TableJudge`
  membership), reading `Context.User`. `IEventPublisher` emits after commit; enums serialize as
  strings (ADR-0007).

### Domain (`Domain/`)

Entities: `Organizer`, `Competition`, `CompetitionCategory`, `CompetitionCategoryStyle`,
`BjcpStyle`, `Participant`, `BeerEntry`, `EntryCollaborator`, `Judge`, `Invitation`,
`TastingTable`, `TableJudge`, `TableSample`, `Evaluation`, `DiscrepancyAlert`, `DispatchJob`,
`GeneratedScoreSheet`, `ResultsArchive`, `AuditLog`. Slice-owned staging entities:
`ImportBatch`/`ImportRow` (`Features/Import/`), `JudgeImportBatch`/`JudgeImportRow`
(`Features/Judges/`). Full schema: `data-model.md`.

### Feature slices (`Features/`) — all under `/api/v1`

| Slice | Endpoints (role) | Notes |
|---|---|---|
| **Catalog** | `GET /styles`, `GET /styles/{code}` (any authenticated) | list sorted by numeric category; detail = vital stats + guide text (FR-049) |
| **Competitions** | `GET/POST /competitions`, `GET/PUT /competitions/{id}`, `POST /competitions/{id}/state`, `GET/PUT /competitions/{id}/categories` (ORGANIZER) | new competitions are `Draft`; edits only in `Draft`/`Active`; `CompetitionStateMachine` enforces FR-006; `Finalized` requires every table `Closed` (`409 tables-still-open`) and enqueues `GeneratePdfs`; emits `CompetitionStateChanged`. Categories (FR-052) are organizer-defined groups of BJCP styles, full-replace `PUT`, a style in at most one category |
| **Import** | `/competitions/{id}/imports`: upload, get, `PUT rows/{n}`, `POST rows/{n}/exclude`, `revalidate`, `consolidate` (ORGANIZER) | ACCE spreadsheet format (ADR-0011, `contracts/import-file.md`). Row statuses `Valid`, `StyleMismatch`, `CategoryMismatch`, `CategoryStyleMismatch`, `Invalid`, `Excluded`. One active batch per competition. Full-row edit; `revalidate` re-checks against current categories. Consolidate blocks on unresolved rows, is one-shot, dedupes participants by email (last import wins) and generates unique blind codes |
| **Judges** | `/competitions/{id}/judges`: list, register (paste list), `PUT {judgeId}` (email), `POST {judgeId}/invitation` (resend), `POST notify`; `/competitions/{id}/judge-imports`: upload, get, edit/exclude row, consolidate (ORGANIZER) | registration and roster import (US14) create `Judge` + `Invitation(Pending)` and enqueue `ProvisionJudgeAccount` (account created, no email, R-20). Emails go out only on explicit **Notify** (all `Pending`, FR-059) or per-judge resend (`SendInvitation`). `UpdateJudgeEmail` is rejected once the judge has logged in (`409 judge-already-active`) |
| **Tables** | `/competitions/{id}/tables`: list, create, `PUT {tableId}` (full desired state), `DELETE {tableId}/judges/{judgeId}`; `GET /competitions/{id}/entries` (ORGANIZER) | `TableAssignmentApplier` validates conflict of interest over the whole submitted set before any write (`409 conflict-of-interest`, FR-017) and flags/unflags `NotValidForBos` competition-wide (FR-018: unflag only when every owner/collaborator judge is clear and none has evaluated). Stats per table: mean real ABV, style count/list. `RemoveJudge` (US12, `InEvaluation` only) soft-removes with a row lock, keeps submitted evaluations, re-reconciles open discrepancies, audits and emits `JudgeRemoved` |
| **TastingOrder** | `/me/tables`: list, `{tableId}/samples`, `{tableId}/judges`, `POST {tableId}/order` (JUDGE) | blind DTOs (`JudgeDtos.cs`): blind code, style, ABV, entry instructions — never beer name or participant fields (FR-019, ADR-0011 exception for `EntryInstructions`). Fix order is one-shot under `SELECT … FOR UPDATE`; emits `TableOrderFixed` to judges and organizers |
| **Evaluations** | `POST /me/tables/{tableId}/evaluations`, `PUT …/evaluations/{id}` (adjust), `GET /me/tables/{tableId}/discrepancies`, `POST /me/tables/{tableId}/close` (JUDGE); `PUT /competitions/{id}/evaluations/{id}` (ORGANIZER correction) | submit: replay check first (idempotent `200`), then gates (state `InEvaluation`, order fixed, table open, strict sequence), insert, unique-violation catch → replay (race-safe, R-07). Discrepancy: a judge is involved if their total differs by > 7 from another judge's on the same sample; `PendingConsensus` + one `DiscrepancyAlert`; only involved judges can adjust. Close: row lock, requires every active judge × sample and no open alert; emits `TableClosed` (`{tableId}` to judges, + consolidated means to organizers). Correction: any table state, same caps, audited (FR-035). Optional structured descriptors + feedback stored as jsonb (ADR-0015) |
| **Monitoring** | `GET /competitions/{id}/progress`, `GET /competitions/{id}/entries/{entryId}/evaluations` (ORGANIZER) | per-table completed/expected/percent (constant query count); read-only audit drill-down with consolidated mean once the table is closed |
| **Dispatch** | `GET /competitions/{id}/results/archive` (`200` ZIP / `202` status), `GET …/dispatch`, `POST …/dispatch/retries` (ORGANIZER) | `GeneratePdfs` (QuestPDF, one sheet per entry, no entrant identity) → `BundleZip` (`{Competition}/{ParticipantId}/{Style}_{BlindCode}.pdf`) → `SendResultEmail` per participant. Bytes stored as `bytea` (ADR-0010). Competition-wide, including entries never assigned to a table. Retry resets a job to a fresh `Pending` |

### SignalR events

`CompetitionStateChanged`, `EvaluationCompleted`, `TableOrderFixed`, `TableClosed`,
`DiscrepancyRaised`, `DiscrepancyResolved`, `JudgeRemoved`, `DispatchProgress` — payloads per
audience in `contracts/signalr-hub.md`.

## Frontend (`frontend/`, Angular 20)

Standalone components + Signals, zone-based change detection (ADR-0003), PWA (`ngsw`, production
only), Tailwind CSS v4, Angular-lockstep packages pinned to 20.x (ADR-0002). UI text is Spain
Spanish in the wizard and all judge screens; the other organizer screens are still English.
Initial bundle ≈ 202 kB gzip (budget 500 kB).

### Core (`core/`)

- **`config/`** (ADR-0017): `main.ts` loads and validates `/config.json` before bootstrap and
  provides `APP_CONFIG`. Cached by the service worker (`freshness`) so an installed PWA boots
  offline.
- **`auth/`**: `keycloak-angular` with `check-sso` + PKCE S256 and auto token refresh (ADR-0012).
  The bearer interceptor only matches `${apiBaseUrl}/api`. Guards: `organizerGuard`,
  `judgeGuard`, `homeRedirectGuard`, `roleSelectGuard`, `settingsGuard`. Dual-role accounts pick
  a role at `/select-role`; `ActiveRoleService` keeps the choice per tab (signal, mirrored to
  `sessionStorage`) — UX only, the backend still enforces real roles. Logout clears it.
- **`layout/bp-page-shell`** (ADR-0014): topbar + `<main>` shell used by every screen, with the
  fixed "Cambiar rol" (dual-role only), "Ajustes" and "Cerrar sesión" actions.
- **`api/`**: `ApiClient` (`get/post/put/delete/getBlob` → `ApiError` from ProblemDetails; the 14
  URNs in `problem-details.model.ts`) and the shared API services (competitions, catalog, entries,
  tables, monitoring, dispatch, import, judge management).
- **`realtime/`**: `CompetitionHubService` — token via `accessTokenFactory`, automatic
  reconnect, re-joins tracked groups on reconnect, typed `on<K>()` streams. Features re-fetch
  state after reconnect.
- **`offline/`** (R-08): Dexie `drafts` (key `beerEntryId`, index `tastingTableId`) and `outbox`
  (key `idempotencyKey`, index `tastingTableId`). `SyncService.saveDraft` debounces ≤ 300 ms.
  `submit()` writes the outbox first, then tries one send (15 s timeout): `200/201` clears it,
  `400/409` is shown to the judge, `404` means the judge was removed (purge + eject), anything else
  stays queued. `replayOutbox()` runs on `online`, service start and after each submit, with
  capped backoff. No Background Sync API.

### Routes and features (`features/`)

| Route | Component (feature) | Purpose |
|---|---|---|
| `/` · `/auth/handoff` | `features/auth/welcome`, `keycloak-handoff` | public landing (authenticated callers are redirected to their workspace); explicit sign-in / create organizer account, Keycloak-hosted |
| `/select-role` | `features/auth/role-select` | dual-role picker |
| `/settings` | `features/settings` | profile from `keycloak-js` (no backend) |
| `/organizer/dashboard` | `features/dashboard` | own competitions with state badge, advance-state action with confirm (FR-051); routes `Draft`/`Active` to the wizard, `InEvaluation`/`Finalized` to the monitor |
| `/organizer/competitions/new` · `/:id` | `features/competition-wizard` | 6 steps: basics → details → categories/styles → entry import → judge roster → table board. `?step=N` in the URL; unsaved-changes guard between steps; read-only ("Modo consulta") in `InEvaluation`/`Finalized` |
| `/organizer/competitions/:id/judges` | `features/judge-management` | paste-list registration, delivery status, resend, email edit |
| `/organizer/competitions/:id/tables` | `features/table-management` | standalone table board (same `TableBoardComponent` as wizard step 6): drag & drop with keyboard "Move to" fallback, click-vs-drag directive, pool filters/sort, COI dialog, BOS banner |
| `/organizer/competitions/:id/monitor` | `features/dashboard` (monitor) | live progress (hub events patch rows), judge removal, read-only audit drill-down, links to wizard and dispatch |
| `/organizer/competitions/:id/dispatch` | `features/results-dispatch` | per-participant email status, retry, ZIP download (blob via `HttpClient`) |
| `/judge/tables` | `features/judge-tables` | assigned tables; ejection banner |
| `/judge/tables/:tableId` | `features/judge-tables` | blind sample list, reorder (drag + move up/down), fix order, sequential "Evaluate", close table, discrepancy banner |
| `/judge/tables/:tableId/samples/:beerEntryId` | `features/evaluation-sheet` | five capped sections with free navigation, structured descriptors (sliders, closed lists, off-flavors, "inappropriate" flags), feedback, summary + submit; offline badge; collapsible BJCP style reference |
| `/judge/tables/:tableId/discrepancies` | `features/discrepancy` | open alerts with totals comparison and adjustment form (online-only) |

Shared primitives live in `shared/components/` (`bp-button`, `bp-input`, `bp-alert`, `bp-topbar`,
`bp-textarea`, `bp-checkbox`, `bp-step-actions`, `bp-file-dropzone`). Design tokens and guidance: `Docs/design/`.

## Testing and quality gates

| Suite | Command | Scope |
|---|---|---|
| Backend unit | `dotnet test backend/tests/BirraPoint.Api.UnitTests` | pure rules, validators, handlers, auth, jobs |
| Backend integration | `dotnet test backend/tests/BirraPoint.Api.IntegrationTests` | `WebApplicationFactory` + Testcontainers PostgreSQL; `TestJwtIssuer` signs test tokens with real `realm_access` claims; contract tests assert blind DTO field absence |
| Frontend unit | `cd frontend && npx jest` | jest-preset-angular, jsdom, `fake-indexeddb`; hand-rolled fakes, no mocking library |
| E2E + a11y | `cd frontend && npm run e2e` | Playwright (Chromium) per story + `e2e/a11y/routes.a11y.spec.ts` axe sweep; needs the full Aspire stack |
| Performance | `k6 run infra/perf/api-budgets.js`, `npm run build:budget` | API p95 budgets (needs a bearer token); gzip initial-bundle gate |
| Lint/format | `ng lint`, `npm run format:check`, `dotnet format --verify-no-changes` | CI gates |
| Infra | `terraform -chdir=infra/azure/terraform test`, `Invoke-Pester infra/azure/tests`, `version.test.sh`, `DaprSecretsEnv.test.sh` | Terraform image/naming rules, teardown logic, release versioning, Keycloak secret loader |

## Known gaps and debt

- **E2E suite not in CI** (T139) and fragile after the wizard/landing redesign; `/select-role` has
  no E2E/axe coverage.
- **No integration tests** for `CompetitionHub` join authorization or for the `DispatchWorker`
  DB loop.
- **DispatchJob under overlapping revisions** (T129): an ACA rollout briefly runs two API replicas;
  jobs have no atomic claim, so a job can run twice.
- **Health endpoints** `/health`, `/alive` are Development-only; ACA uses default probes (T098).
- **Single API replica**: scaling out needs a SignalR backplane, job leases and a migration job.
- **Keycloak production hardening** pending (T130: brute-force, password policy, admin console
  exposure); Keycloak's JDBC uses `sslmode=require` (API uses `VerifyFull`).
- **`UpdateJudgeEmail`** does not re-run COI/BOS checks against the new email (contract says it
  should).
- **Monitor "Order fixed by"** note comes only from the live event; no REST backfill.
- **Outbox rows** rejected definitively before ever persisting keep retrying with no UI to discard
  them.
- **`EntryInstructions`** is not shown in the import row summary, so nothing nudges the organizer
  to review it before consolidation.
- **Offline cold reload** is not provable in the dev-mode E2E harness (service worker disabled);
  no WebKit project in Playwright yet (iOS Safari is the offline target).
- **Lighthouse TTI** (< 3 s on 4G) not measured: every route needs login first.
- **Descriptor labels** in the results PDF are English tokens, not localized.
- **Line endings**: `.gitattributes` pins LF only for container-executed files and the BJCP JSON.
