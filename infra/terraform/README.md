# BirraPoint — cloud deployment (Terraform → Azure Container Apps + Neon)

Constitution v1.4.0, research R-17/R-18/R-19, ADR-0016/ADR-0017/ADR-0021, FR-043–FR-047, SC-011.

## Topology

```text
                     Internet
          ┌─────────────┴──────────────┐
   https  │                            │ https
  ┌───────▼─────────────┐   ┌──────────▼──────────┐
  │ birrapoint-prod-web │   │ birrapoint-prod-kc  │  Keycloak 26 (realm imported on first
  │ nginx: PWA +        │   │ external ingress    │  start) + Dapr sidecar
  │ /api,/hubs proxy    │   └──────────┬──────────┘──────────────┐
  └───────┬─────────────┘              │ secrets (Dapr)          │ direct endpoint
          │ https (internal only)      ▼                         │
  ┌───────▼─────────────┐   ┌─────────────────────┐   ┌──────────▼──────────────┐
  │ birrapoint-prod-api │──▶│ birrapoint-prod-kv  │   │ Neon project            │
  │ 1 replica + Dapr    │   │ Key Vault (RBAC)    │   │ birrapoint-prod-neon    │
  │ sidecar             │   └─────────────────────┘   │  db `birrapoint` (API)  │
  └───────┬─────────────┘  secrets (Dapr)             │  db `keycloak`          │
          └──────── pooled endpoint (migrations: direct) ▶                      │
   birrapoint-prod-cae (Container Apps environment)    └─────────────────────────┘
   + birrapoint-prod-log (Log Analytics), all in       outside Azure (R-18)
   birrapoint-prod-rg
```

- **Naming** (ADR-0021): every resource is `birrapoint-<environment>-<acronym>`, the environment
  lower-cased (`environment` variable / `-Environment`, default `PROD`): `-rg` resource group,
  `-log` Log Analytics, `-cae` Container Apps environment, `-kv` Key Vault, `-api` / `-web` /
  `-kc` Container Apps, `-neon` Neon project; the Terraform state lives in
  `birrapoint-<env>-tfstate-rg` / storage account `birrapoint<env>st<subscription>` / blob
  `birrapoint-<env>.tfstate`. The environment is 2-10 letters/digits (Key Vault names are limited
  to 24 characters and globally unique, as are storage account names).

- **Images** come from Docker Hub and are never built by the deployment: CI publishes
  `<namespace>/birrapoint-api|web|keycloak:latest` on every merge to `main`, the release pipeline
  publishes immutable `<namespace>/birrapoint-*-release:X.Y.Z` (FR-064). None
  contains secrets or environment-specific configuration (FR-043). The web image renders
  `/config.json` and its nginx upstream from env vars at start; the Keycloak realm resolves its
  `${VAR:default}` placeholders at import; the API reads its configuration from env vars and its
  secrets from Key Vault (below).
- **The API is not publicly reachable.** Browsers only talk to the web app (same origin, no
  CORS) and to Keycloak.
- **The API runs exactly one replica**: SignalR has no backplane and the `DispatchJob` worker is a
  single consumer (R-06). Scaling out requires a backplane first.
- **Secrets** (Neon credentials, Keycloak bootstrap admin password, API admin-client secret,
  deploy-client secret, SMTP password) live in **Key Vault** `birrapoint-<env>-kv` (and in
  Terraform's remote state, which writes them; the generated ones come from `random_password`).
  No app receives a secret as a setting (ADR-0021):
  - every Container App has a **system-assigned managed identity** with the read-only
    `Key Vault Secrets User` role on the vault (the web app has no secret today but the same
    access);
  - a **Dapr secret store component** (`secretstore`, type `secretstores.azure.keyvault`, scoped to
    the API and Keycloak) reads the vault with the calling app's identity;
  - the **API** loads `ConnectionStrings--db`, `ConnectionStrings--dbDirect`,
    `Keycloak--AdminClientSecret` and (optional) `Smtp--Password` at startup through the Dapr .NET
    configuration provider (`Dapr__SecretStore`; `--` maps to the `:` configuration separator);
  - **Keycloak**'s image entrypoint loads the secrets listed in `DAPR_SECRETS`
    (`ENV_VAR=secret-name`) from the sidecar before starting Keycloak
    (`infra/keycloak/dapr-secrets/`).

  Only the Docker Hub pull token stays a Container Apps registry secret: the platform needs it to
  pull the image, before any sidecar exists. Rotating a secret: change it in Key Vault (or let
  Terraform do it), then restart the app's revision — secrets are read once at startup.

## Prerequisites (one-time)

| What | How |
|---|---|
| Azure subscription + CLI | `az login` as **Owner** (or Contributor + User Access Administrator) of the subscription: Terraform creates role assignments on the Key Vault |
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
2. creates the state storage (`birrapoint-<env>-tfstate-rg`) if missing and runs `terraform init` +
   `terraform apply` (with `environment` from `-Environment`, default `PROD`). On a first apply
   Terraform waits ~90 s after granting itself `Key Vault Secrets Officer`, for the role to reach
   the vault, before writing the secrets. The apps' own role assignments can only be created
   once the apps (and so their identities) exist, so a brand-new app's first revision may fail to
   read its secrets; step 3 replaces any revision that is not healthy;
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

The image-resolution and rollout decisions live in `infra/DeployImages.psm1`, resource names in
`infra/ResourceNames.psm1`, with Pester tests
in `infra/tests/` (run by the `infra.yml` workflow when `infra/**` changes). Windows PowerShell
5.1 ships Pester 3.4, which cannot run them:

```powershell
Install-Module Pester -MinimumVersion 5.0 -Scope CurrentUser -SkipPublisherCheck   # once
Invoke-Pester infra/tests
```

GitHub Actions publishes the images and deploys releases (`deploy.yml` runs the `-AppsOnly` mode
below); its one-time setup — Docker Hub token, Azure OIDC identity, `production` environment — is
in [`infra/github-actions-setup.md`](../github-actions-setup.md).

### Image-only deployment (`-AppsOnly`)

```powershell
./infra/deploy.ps1 -ImageNamespace <ns> -AppsOnly -ApiVersion 0.3.1 -WebVersion 0.3.1 -KeycloakVersion 0.3.1
```

Skips step 2 entirely: no Terraform, `terraform.tfvars`, `NEON_API_KEY` or state access —
only `az login` with rights on the application resource group (`birrapoint-<env>-rg`; pass
`-Environment` for an environment other than `PROD`). This is how the deploy pipeline releases a version,
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
`neon_history_retention_seconds` (default 21600 s = 6 hours, the free-plan maximum; raise it on a paid plan)
and can restore any moment inside that window:

1. Neon console → project `birrapoint-prod-neon` (id: `terraform output neon_project_id`) → **Restore**.
2. Pick branch `main` and the timestamp to restore to, then confirm. Neon keeps a backup
   branch of the pre-restore state, so the restore itself is reversible.
3. Both databases (`birrapoint` and `keycloak`) live on that branch and are restored together,
   which keeps application data and Keycloak user ids consistent.
4. Restart the API and Keycloak revisions so they drop pooled connections:
   `az containerapp revision restart` for `birrapoint-prod-api` and `birrapoint-prod-kc`, or redeploy.

For a non-destructive check first, create a branch from a past timestamp (Neon console →
Branches → New branch → "Past data") and inspect it with `psql` before restoring `main`.

## Tear down

```powershell
$env:NEON_API_KEY = "<key>"
./infra/teardown.ps1            # stop all Azure costs; keep Neon data + state (asks to type the resource group name)
./infra/teardown.ps1 -WhatIf    # preview, changes nothing
./infra/teardown.ps1 -IncludeNeon   # full wipe, production data included (second confirmation)
```

**Default** — removes only what Azure bills for: `birrapoint-prod-rg` with the three Container
Apps, the environment, Log Analytics and the Key Vault
(`terraform destroy -target=azurerm_resource_group.main`; pass `-Environment` for another one). It
**keeps** the Neon project and its data (free plan; its compute suspends when idle) and the
Terraform state. The state must stay: it remembers the Neon project and the generated
passwords — the API admin-client secret among them is already stored in Keycloak's database — so
the next `deploy.ps1` recreates only the Azure part and reconnects to the same data with matching
secrets. Deleting the state while keeping Neon would leave a redeploy with mismatched secrets.

**`-IncludeNeon`** — destroys everything Terraform manages (the Neon project too), deletes
`birrapoint-<env>-tfstate-rg` and the local `infra/terraform/.terraform`, for a fresh start with empty
data.

The resource group and the Neon project to remove are read from the Terraform state blob itself
(read-only, before you confirm), not from parameters, so a custom `neon_org_id` in
`terraform.tfvars` is honoured (the state itself is found from `-Environment`). With `-IncludeNeon` the Neon project is deleted **by the id in
the state**; only without a state does the script look it up by exact name, and it refuses when
several projects share that name.

Both modes are idempotent: if `terraform destroy` fails or the state is missing, the script sweeps
leftovers directly (`az group delete` after purging Log Analytics; the Neon project by id) and ends
by verifying nothing remains. Log Analytics and Key Vault are purged, not soft-deleted
(`permanently_delete_on_destroy`, `purge_soft_delete_on_destroy`, and an explicit
`az keyvault purge` after a direct resource-group delete), so a redeploy never collides with
them — the vault's name is globally unique. Note that the same
provider setting also applies if Terraform ever *replaces* the workspace during a normal apply:
its logs are then purged immediately instead of being recoverable for 14 days.

**Redeploying after a default teardown.** The recreated Container Apps environment gets a new
random domain, while Keycloak keeps its realm in Neon (the realm is imported only on first
start). `deploy.ps1` therefore ends every full run by making Keycloak's `birrapoint-spa` client
allow the current web URL through the Keycloak Admin API (ADR-0020):

- it authenticates as the **`birrapoint-deploy`** service-account client (secret generated by
  Terraform, `terraform output -raw keycloak_deploy_client_secret`), which can manage clients in
  the `birrapoint` realm and has no rights in `master`. Treat its secret like the API admin
  secret, and **never copy it into CI**: through the clients it manages it can indirectly reach
  user management and the SPA's allowed URLs (ADR-0020). `deploy.yml` never needs it;
- redirect URIs, web origins and post-logout URIs are **merged**: the current URL is added and only
  the web app's previous Container Apps domains are dropped, so URIs you add by hand (a custom
  domain, localhost) are kept; root/base URL are set to the current URL; nothing is written when
  already in sync;
- on a realm imported before that client existed (or with a drifted secret) it creates/resets the
  client **once** with the bootstrap admin. If Keycloak rejects both, the deploy stops with
  instructions: create client `birrapoint-deploy` in the admin console (confidential, service
  accounts only, `realm-management` roles `view-clients` + `manage-clients`) with the secret from
  the output above, then re-run. After that the bootstrap admin may be removed.
Docker Hub images, GitHub secrets and the deploy identity are never touched.

## Migrating an environment deployed before ADR-0021

Deployments made with the old names (`rg-birrapoint`, `birrapoint-api`...) keep their state in
`rg-birrapoint-tfstate` / `stbirrapointtf<subscription>` / `birrapoint.tfstate`, which the new
default state location does not find. Either:

- **start clean** — remove the old environment with the previous version of `teardown.ps1`
  (`git show <old-commit>:infra/teardown.ps1`), then run `deploy.ps1`; or
- **keep Neon and the generated secrets** — point one run at the old state:
  `./infra/deploy.ps1 -ImageNamespace <ns> -StateResourceGroup rg-birrapoint-tfstate
  -StateStorageAccount stbirrapointtf<first 10 hex of the subscription id> -StateKey birrapoint.tfstate`.
  Terraform replaces every Azure resource under its new name (new URLs; `deploy.ps1` re-syncs
  Keycloak's `birrapoint-spa` client), renames the Neon project in place and keeps its data.
  Keep passing those three parameters for that environment, or copy the blob to the new location
  (`birrapoint-prod-tfstate-rg` / `birrapointprodst<subscription>` / `birrapoint-prod.tfstate`)
  and delete the old state resource group.

The deploy identity of `deploy.yml` has rights on the old resource group only: re-grant them on
`birrapoint-prod-rg` (`infra/github-actions-setup.md` step 2).

## Known limitations

- Default `*.azurecontainerapps.io` host names; no custom domain yet.
- **Key Vault is reachable over its public endpoint** (Azure RBAC still required): the Container
  Apps environment has no virtual network, so there is no private endpoint yet.
- **Secrets are read once at startup**: a rotated secret reaches an app on its next revision or
  restart.
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
