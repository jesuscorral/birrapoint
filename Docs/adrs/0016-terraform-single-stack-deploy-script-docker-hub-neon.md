# 0016 - Single Terraform stack + `deploy.ps1`, images on Docker Hub, Neon via Terraform provider

**Status:** Accepted — partially superseded by ADR-0018 (image build/push, `image_tag`),
ADR-0021 (naming, secrets) and ADR-0022 (`deploy.ps1`, Azure Storage state backend)
**Date:** 2026-09-24

## Context

Constitution v1.3 replaced Bicep/`azd` with Terraform and moved production PostgreSQL to Neon.
Open points: where images live (decided: Docker Hub, v1.3.1), how "one command, zero manual steps"
(FR-045/SC-011) holds when Terraform neither builds images nor creates its own state storage, and
how Neon is provisioned. Runtime constraints: one DispatchJob consumer (R-06), SignalR without a
backplane, and Neon's pooled endpoint is PgBouncer in transaction mode.

## Decision

1. **One Terraform root module** (`infra/terraform/`) for resource group, Log Analytics, ACA
   environment, Neon project/databases/roles, generated secrets and the three Container Apps.
2. **`infra/deploy.ps1` is the single command**: checks prerequisites, creates the remote-state
   storage idempotently with `az`, runs `terraform init` + `apply`. Works on Windows PowerShell 5.1
   and PowerShell 7.
3. **Docker Hub** (`docker.io/<ns>/birrapoint-*`), public or private; optional
   `dockerhub_username`/`dockerhub_token` add a registry credential.
4. **Neon via the `kislerdm/neon` provider** (`NEON_API_KEY` from the environment): one project,
   branch `main`, databases `birrapoint` and `keycloak` with their own roles, on the same branch so
   a point-in-time restore keeps app data and Keycloak user ids consistent.
5. **Connections**: the API uses the pooled endpoint at runtime and the direct endpoint
   (`ConnectionStrings:dbDirect`) for startup migrations, whose session-level lock breaks under
   transaction pooling. Keycloak uses the direct endpoint.
6. **Migrations on API startup**, not a separate job: the API is pinned to one replica.
7. **Production Keycloak image** (`infra/keycloak/Dockerfile`): optimized build with the theme and
   a production realm derived at build time (seeded account and local secret fallbacks stripped;
   environment values as `${VAR:default}` placeholders — Keycloak resolves `${VAR}`, not
   `${env.VAR}`).

## Consequences

- One-time prerequisites (`az login`, `NEON_API_KEY`, `terraform.tfvars`) and the Neon PITR
  procedure (FR-047) are in `infra/terraform/README.md`.
- Destroying the Neon project deletes its data; `teardown.ps1` keeps it by default.
- Docker Hub and Neon credentials are the only external secrets besides SMTP.
- ACA overlaps two API revisions during a rollout, so a job can run twice until jobs are claimed
  atomically (T129).
- Scaling the API beyond one replica needs a SignalR backplane, job leases and a migration job.
