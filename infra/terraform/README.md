# BirraPoint — cloud deployment (Terraform → Azure Container Apps + Neon)

Constitution v1.3.1, research R-17/R-18/R-19, ADR-0016/ADR-0017, FR-043–FR-047, SC-011.

## Topology

```text
                 Internet
          ┌─────────┴──────────┐
   https  │                    │ https
  ┌───────▼────────┐   ┌───────▼────────┐
  │ birrapoint-web │   │ birrapoint-kc  │  Keycloak 26 (realm imported on first start)
  │ nginx: PWA +   │   │ external       │
  │ /api,/hubs     │   │ ingress        │──────┐
  │ reverse proxy  │   └────────────────┘      │ direct endpoint
  └───────┬────────┘                           │
          │ https (internal ingress only)      │
  ┌───────▼────────┐                    ┌──────▼──────────────────┐
  │ birrapoint-api │── pooled endpoint ─▶ Neon project            │
  │ 1 replica      │   (migrations on   │  db `birrapoint` (API)  │
  └────────────────┘    direct endpoint)│  db `keycloak`          │
   Azure Container Apps environment     └─────────────────────────┘
   (+ Log Analytics)                     outside Azure (R-18)
```

- **Images** come from Docker Hub and are never built by the deployment: CI publishes
  `<namespace>/birrapoint-api|web|keycloak:latest` on every merge to `main`, the release pipeline
  publishes immutable `<namespace>/birrapoint-*-release:X.Y.Z` (FR-064). None
  contains secrets or environment-specific configuration (FR-043). The web image renders
  `/config.json` and its nginx upstream from env vars at start; the Keycloak realm resolves its
  `${VAR:default}` placeholders at import; the API reads everything from env vars.
- **The API is not publicly reachable.** Browsers only talk to the web app (same origin, no
  CORS) and to Keycloak.
- **The API runs exactly one replica**: SignalR has no backplane and the `DispatchJob` worker is a
  single consumer (R-06). Scaling out requires a backplane first.
- **Secrets** (Neon credentials, Keycloak bootstrap admin password, API admin-client secret,
  SMTP password, Docker Hub token) live only in Terraform's remote state and Container Apps
  secrets. The two generated ones come from `random_password`.

## Prerequisites (one-time)

| What | How |
|---|---|
| Azure subscription + CLI | `az login` (Contributor on the subscription) |
| Terraform ≥ 1.9 | <https://developer.hashicorp.com/terraform/install> |
| Docker Hub images | Published by GitHub Actions (`ci.yml` for `latest`, `release.yml` for `X.Y.Z`); no local Docker needed |
| Neon account + API key | Neon console → Account settings → API keys; `$env:NEON_API_KEY = "..."` |
| SMTP relay | Any provider with SMTP credentials and a verified sender address |
| Variables file | `cp infra/terraform/terraform.tfvars.example infra/terraform/terraform.tfvars`, fill in |

Private Docker Hub repositories additionally need `dockerhub_username` + a **read-only** access
token in `dockerhub_token`; with public repositories leave both empty.

## Deploy (single command)

```powershell
$env:NEON_API_KEY = "<key>"
./infra/deploy.ps1 -ImageNamespace <dockerhub-user-or-org>          # latest images, interactive apply
./infra/deploy.ps1 -ImageNamespace <dockerhub-user-or-org> -AutoApprove `
    -ApiVersion 0.3.1 -WebVersion 0.3.1 -KeycloakVersion 0.3.0      # release images
./infra/deploy.ps1 -ImageNamespace <dockerhub-user-or-org> -WhatIf  # preview, changes nothing
```

The script is idempotent. Each component's image is chosen independently: `-ApiVersion`,
`-WebVersion`, `-KeycloakVersion` take a release version `X.Y.Z` and deploy
`<ns>/birrapoint-<component>-release:X.Y.Z`; an omitted version deploys
`<ns>/birrapoint-<component>:latest`. It then:

1. checks that every selected image exists on Docker Hub (fails before changing anything);
2. creates the state storage (`rg-birrapoint-tfstate`) if missing and runs `terraform init` +
   `terraform apply`;
3. rolls each Container App to its image with `az containerapp update` — Keycloak, then the API
   (which migrates the database on startup), then the web app — waiting for each new revision
   to become healthy before the next (the web app may scale to zero, so for it "provisioned with
   no replicas" also counts). An app whose latest revision is healthy and already serves the
   target release is skipped; `latest` always gets a new revision so the moved tag is re-pulled
   (usually skipped right after the apply created one that pulled it).

It prints `web_url` and `keycloak_url` at the end.

**Terraform owns the infrastructure, not the running image.** The image variables
(`api_image`, `web_image`, `keycloak_image`) are used only when a Container App is first
created; `lifecycle.ignore_changes` makes later applies leave the image alone, so an
infrastructure change never rolls an app back to an older version.

Every full run passes a fresh `revision_suffix` (`infra-<UTC timestamp>`), so **each apply
creates a new revision of all three apps (a short restart)** — a rollout leaves its own suffix in
the state, and Container Apps rejects a template change that reuses one (ADR-0018). Always apply
through `deploy.ps1`; a bare `terraform apply` asks for `revision_suffix`. A full run refuses to
start while an app's latest revision is not its serving one (a failed earlier rollout would
otherwise be re-created from the failed image): recover that app with `-AppsOnly` first.
Answering *No* to `-Confirm`'s apply prompt aborts the run.

`-WhatIf` checks the images and reads the apps' current state but changes nothing (no state
bootstrap, `terraform init`/`apply` or rollout).

### Tests of the deploy logic

The image-resolution and rollout decisions live in `infra/DeployImages.psm1`, with Pester tests
in `infra/tests/` (run by the `infra.yml` workflow when `infra/**` changes). Windows PowerShell
5.1 ships Pester 3.4, which cannot run them:

```powershell
Install-Module Pester -MinimumVersion 5.0 -Scope CurrentUser -SkipPublisherCheck   # once
Invoke-Pester infra/tests
```

### Image-only deployment (`-AppsOnly`)

```powershell
./infra/deploy.ps1 -ImageNamespace <ns> -AppsOnly -ApiVersion 0.3.1 -WebVersion 0.3.1 -KeycloakVersion 0.3.1
```

Skips step 2 entirely: no Terraform, `terraform.tfvars`, `NEON_API_KEY` or state access —
only `az login` with rights on the application resource group (`rg-<name_prefix>`; pass
`-NamePrefix` if you changed `name_prefix`). This is how the deploy pipeline releases a version,
and how to roll back: re-run with the previous versions. The environment must already exist
(created once by a full run).

First start takes a few minutes: Keycloak creates its schema on Neon and imports the realm, and
the API applies EF Core migrations (including the BJCP catalog seed) before serving.

The Keycloak admin console is at `<keycloak_url>/admin`, user `admin`, password from
`terraform -chdir=infra/terraform output -raw keycloak_admin_password`.

There is **no seeded organizer account** in production (the image strips the local-dev
`organizer`/`organizer` user); organizers self-register from the login page.

## Restore — Neon point-in-time recovery (FR-047)

There is no self-managed backup job. Neon keeps a write-ahead-log history for
`neon_history_retention_seconds` (default 1 day, the free-plan maximum; raise it on a paid plan)
and can restore any moment inside that window:

1. Neon console → project `birrapoint` (id: `terraform output neon_project_id`) → **Restore**.
2. Pick branch `main` and the timestamp to restore to, then confirm. Neon keeps a backup
   branch of the pre-restore state, so the restore itself is reversible.
3. Both databases (`birrapoint` and `keycloak`) live on that branch and are restored together,
   which keeps application data and Keycloak user ids consistent.
4. Restart the API and Keycloak revisions so they drop pooled connections:
   `az containerapp revision restart` for `birrapoint-api` and `birrapoint-kc`, or redeploy.

For a non-destructive check first, create a branch from a past timestamp (Neon console →
Branches → New branch → "Past data") and inspect it with `psql` before restoring `main`.

## Tear down

```powershell
terraform -chdir=infra/terraform destroy -var="subscription_id=<id>" -var="api_image=unused" -var="web_image=unused" -var="keycloak_image=unused" -var="revision_suffix=destroy" -var-file=terraform.tfvars
```

This deletes the Azure resources **and the Neon project with all its data**. The state storage
account (`rg-birrapoint-tfstate`) is left in place.

## Known limitations

- Default `*.azurecontainerapps.io` host names; no custom domain yet.
- **Realm changes after the first start are not applied.** `--import-realm` skips a realm that
  already exists, so rotating `random_password.api_admin_client_secret` or changing the web URL
  (e.g. a custom domain) does not reach Keycloak — update the client in the admin console too,
  or judge provisioning / login redirects break.
- **Revision rollout overlap.** Even in `Single` revision mode ACA briefly runs the old and the
  new API revision together, and the DispatchJob worker has no atomic job claim yet, so a job in
  flight during a deploy can run twice (duplicate result email). Avoid deploying while results
  are being dispatched until T129 lands.
- **Keycloak hardening** (brute-force detection, password policy, admin console exposure) is
  T130. Until then, change the `admin` password after the first login and keep it strong.
- **Docker Hub rate limits.** Anonymous pulls from ACA's shared outbound IPs can be throttled;
  setting `dockerhub_username` + a read-only `dockerhub_token` avoids it even for public
  repositories (T131).
- **Neon compute.** The API's job-queue poll and Keycloak's pool keep the Neon compute awake
  around the clock, which can exceed the free plan's monthly compute allowance (T132).
- OpenTelemetry export to Azure and health probes for the API arrive with T098; until then the
  API uses ACA's default TCP probe and logs go to Log Analytics via console output.
- Keycloak's JDBC connection uses `sslmode=require` (encrypted, server certificate not
  verified); the API's Npgsql connections use `VerifyFull`.
