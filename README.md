# BirraPoint

Progressive web app for running beer competitions with blind tastings (*catas a ciegas*).

## What it does

- **Organizers** create a competition in a six-step wizard: details, categories and allowed BJCP
  2021 styles, `.xlsx` entry import with row-by-row correction, judge roster (paste or `.xlsx`)
  with invitations, and tasting tables with conflict-of-interest protection.
- **Judges** see only blind codes and styles, fix a shared tasting order per table, and fill a
  BJCP score sheet (capped sections, structured descriptors) that works **offline** and syncs
  exactly once when back online.
- Totals more than 7 points apart raise a **discrepancy** that the judges involved must resolve
  before closing the table. Closed tables are immutable; only audited organizer corrections are
  allowed.
- A **live dashboard** shows progress per table, with an audit view of every evaluation and live
  judge removal.
- **Finalizing** generates one PDF per entry and a results ZIP, then emails every participant their
  sheets, with per-recipient status and retry.

Out of scope: Best of Show and tie-breaks (only a `NotValidForBos` flag is recorded).

## Tech stack

| Layer | Technology |
|---|---|
| Backend | .NET 10 / C# 14, ASP.NET Core Minimal APIs, vertical slices + MediatR, EF Core + PostgreSQL 16, SignalR, QuestPDF, MailKit |
| Frontend | Angular 20 (standalone + Signals), PWA, Dexie (IndexedDB) offline engine, Tailwind CSS |
| Identity | Keycloak (OIDC, Authorization Code + PKCE), roles `ORGANIZER` / `JUDGE` |
| Local | .NET Aspire (PostgreSQL, Keycloak, Mailpit, API, PWA) |
| Cloud | Azure Container Apps, Key Vault + Dapr, Neon PostgreSQL, Terraform, images on Docker Hub, GitHub Actions |
| Testing | xUnit + Testcontainers, Jest, Playwright + axe-core, k6 |

## Run locally

Prerequisites: Docker Desktop, .NET 10 SDK, Node.js 24+.

```bash
dotnet run --project backend/src/BirraPoint.AppHost
```

This starts the whole stack. The PWA runs at <http://localhost:4200> (seeded login
`organizer` / `organizer`), Keycloak at <http://localhost:8081> and Mailpit at
<http://localhost:8025>. The Aspire dashboard URL is printed on startup.

Tests: `dotnet test backend/BirraPoint.sln` · `cd frontend && npx jest` · `cd frontend && npm run e2e`
(E2E needs the running stack).

## Deploy to Azure

BirraPoint deploys to Azure or to AWS, one cloud per deployment (ADR-0023). Azure is available now
(`infra/azure/`); the AWS target (`infra/aws/`) is planned (T145/T146).

One-time prerequisites: Azure CLI logged in as Owner (or Contributor + User Access Administrator),
Terraform ≥ 1.9, a Neon API key, the non-secret inputs in `infra/azure/terraform/environments/prod.tfvars`
(set `image_namespace` and the SMTP host/sender) and the secrets in `infra/azure/terraform/terraform.tfvars` (copy
`terraform.tfvars.example`). Images are built by CI; no local Docker is needed.

Deployment is Terraform only (ADR-0022); state is in HCP Terraform (workspace in Local execution
mode: `TF_CLOUD_ORGANIZATION`, `TF_WORKSPACE`, `terraform login`).

```bash
export NEON_API_KEY=<key>
terraform -chdir=infra/azure/terraform init
terraform -chdir=infra/azure/terraform apply -var-file=environments/prod.tfvars -var release_version=1.2.3   # release images
terraform -chdir=infra/azure/terraform apply -var-file=environments/prod.tfvars -var release_version=latest   # CI images
./infra/azure/teardown.ps1 [-WhatIf]                                 # removes everything, data included
```

Releases and production deployments run from GitHub Actions:

```bash
gh workflow run release.yml -f ref=main -f bump=patch   # publish version.txt's X.Y.Z
gh workflow run deploy-azure.yml -f version=X.Y.Z       # apply it to Azure (approval required)
```

Details: [`infra/azure/terraform/README.md`](./infra/azure/terraform/README.md) and
[`infra/github-actions-setup.md`](./infra/github-actions-setup.md).

## Documentation

- [`specs/001-birrapoint-mvp/`](./specs/001-birrapoint-mvp/) — specification, plan, tasks, data
  model and API/SignalR contracts (source of truth).
- [`Docs/arquitectura_viva.md`](./Docs/arquitectura_viva.md) — current architecture.
- [`Docs/adrs/`](./Docs/adrs/) — architecture decision records.
- [`CLAUDE.md`](./CLAUDE.md) — development guide: commands, conventions, invariants, workflow.
