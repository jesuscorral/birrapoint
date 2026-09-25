# 0016 - Single Terraform stack + `deploy.ps1`, images on Docker Hub, Neon via Terraform provider

**Status:** Accepted
**Date:** 2026-09-24

## Context

Constitution v1.3.0 (2026-09-23) replaced Bicep/`azd up` with Terraform and moved production
PostgreSQL to Neon, but left three things open for Phase 16 (T095–T097):

- **Where images live.** v1.3.0 assumed Azure Container Registry. The user decided on
  2026-09-24 to publish to Docker Hub instead (constitution v1.3.1).
- **How "single command, zero manual steps" (FR-045/SC-011) holds** when Terraform, unlike
  `azd up`, neither builds images nor creates its own remote-state storage.
- **Whether Neon is provisioned by Terraform or by hand** (T097: "per the plan's decision").

There are also runtime constraints: the API runs a single DispatchJob consumer (R-06) and SignalR
has no backplane, and Neon's pooled endpoint is PgBouncer in transaction mode.

## Decision

1. **One Terraform root module** (`infra/terraform/`) for the resource group, Log Analytics, the
   ACA environment, the Neon project/databases/roles, generated secrets (`random_password`) and
   the three Container Apps. With no registry to create first, splitting the stack into
   "foundation" and "apps" (as planned with ACR) is unnecessary.
2. **`infra/deploy.ps1` is the single command.** It checks prerequisites, creates the remote-state
   resource group, storage account and container idempotently with `az`, builds and pushes the
   three images to Docker Hub tagged with the git commit SHA (a clean tree is required unless
   overridden), then runs `terraform init` (partial `azurerm` backend configuration) and
   `terraform apply`. It is written for Windows PowerShell 5.1 as well as PowerShell 7.
3. **Docker Hub, public or private.** Container Apps pull `docker.io/<namespace>/birrapoint-*`;
   when `dockerhub_username`/`dockerhub_token` are set, a registry block with the token as a
   Container Apps secret is added to every app.
4. **Neon through the `kislerdm/neon` Terraform provider** (`NEON_API_KEY` read from the
   environment, never a variable): one project, default branch `main` with databases
   `birrapoint` (owned by role `birrapoint`) and `keycloak` (role `keycloak`). Both databases
   share one branch, so a point-in-time restore keeps application data and Keycloak user ids
   consistent.
5. **Connection endpoints.** The API's runtime string (`ConnectionStrings:db`) uses Neon's
   pooled endpoint. EF Core migrations run on startup (`Database:MigrateOnStartup=true`) over a
   separate direct-endpoint string (`ConnectionStrings:dbDirect`), because the migration lock is
   a session-level advisory lock that transaction pooling breaks. Keycloak (Hibernate,
   server-side prepared statements) uses the direct endpoint.
6. **Migrations on startup, not a separate job.** The API is pinned to exactly one replica
   (`min = max = 1`) for the DispatchJob and SignalR reasons above, so there is no concurrent
   migrator. A separate ACA job would add a manual trigger step or extra orchestration for no
   gain at this scale.
7. **The Keycloak production image** (`infra/keycloak/Dockerfile`) is an optimized build
   (`kc.sh build`, health + metrics) with the login theme and a production realm derived from the
   local-dev realm at image build time. The derivation strips the seeded
   `organizer`/`organizer` account and the admin-client secret's local fallback. The realm's
   environment-specific values (SPA URL, admin client secret, SMTP) are Keycloak
   `${VAR:default}` import placeholders. Note: Keycloak resolves `${VAR}`, **not** `${env.VAR}`.
   The earlier `${env.SMTP_HOST}` form in the realm was never substituted, so it was fixed in
   the same change.

## Consequences

- One command deploys from a workstation (`./infra/deploy.ps1 -ImageNamespace <ns>`). The
  one-time prerequisites (`az login`, `docker login`, `NEON_API_KEY`, `terraform.tfvars`) are
  documented in `infra/terraform/README.md`, which also holds the Neon PITR restore procedure
  (FR-047).
- `terraform destroy` removes the Neon project and all its data. That is intentional for a clean
  teardown, but it is not a backup.
- Two external providers (Docker Hub, Neon) sit outside the Azure boundary. Their credentials are
  the only secrets the operator supplies, besides SMTP.
- "One replica" is not absolute: ACA overlaps the old and new revision during a rollout, so
  until the DispatchJob worker claims jobs atomically (T129) a job in flight during a deploy can
  run twice.
- Scaling the API beyond one replica now needs a SignalR backplane, a DispatchJob claim/lease
  model, and moving migrations to a dedicated job. This is recorded as a known limitation, not
  a bug.
- A CI pipeline can reuse `deploy.ps1` as is (`-AutoApprove`). Moving to OIDC-based
  non-interactive Azure login is a later concern.
