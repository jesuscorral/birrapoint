# BirraPoint — cloud deployment (Terraform → Azure Container Apps + Neon)

Constitution v1.5.1, research R-17/R-18/R-19/R-21, ADR-0016/ADR-0017/ADR-0021/ADR-0022/ADR-0023, FR-043–FR-047, SC-011.

First deploy from scratch? Follow the end-to-end checklist in [`infra/azure/deployment-runbook.md`](../deployment-runbook.md) (accounts, keys, order).

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
  lower-cased (`environment` variable, default `PROD`): `-rg` resource group,
  `-log` Log Analytics, `-cae` Container Apps environment, `-kv` Key Vault, `-api` / `-web` /
  `-kc` Container Apps, `-neon` Neon project. The environment is 2-10 letters/digits (Key Vault
  names are limited to 24 characters and globally unique). If `birrapoint-<env>-kv` is already
  taken in another subscription, set `key_vault_name` in `terraform.tfvars`.

- **Images** come from Docker Hub and are never built by the deployment: CI publishes
  `<namespace>/birrapoint-api|web|keycloak:latest` on every merge to `main`, the release pipeline
  publishes immutable `<namespace>/birrapoint-*-release:X.Y.Z` (FR-064). None
  contains secrets or environment-specific configuration (FR-043). The web image renders
  `/config.json` and its nginx upstream from env vars at start; the Keycloak realm resolves its
  `${VAR:default}` placeholders at import; the API reads its configuration from env vars and its
  secrets from Key Vault (below).
- **The API is not publicly reachable.** Browsers only talk to the web app (same origin, no
  CORS) and to Keycloak.
- **The API runs exactly one replica**: SignalR has no backplane. The `DispatchJob` queue itself is
  multi-worker safe (lease + `SKIP LOCKED`, ADR-0024). Scaling out requires a backplane first.
- **Secrets** (Neon credentials, Keycloak bootstrap admin password, API admin-client secret,
  SMTP password) live in **Key Vault** `birrapoint-<env>-kv` (and in
  Terraform's state in HCP, which writes them; the generated ones come from `random_password`).
  No app receives a secret as a setting (ADR-0021):
  - every Container App has a **system-assigned managed identity**, and each identity holds the
    read-only `Key Vault Secrets User` role **per secret**: only on the secrets its app uses
    (`app_secret_names` in `secrets.tf`). The web app has no secret, so it has no grant;
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
| Azure subscription + CLI | `az login` as **Owner** (or Contributor + User Access Administrator) of the subscription: Terraform creates role assignments on the Key Vault. The subscription is `ARM_SUBSCRIPTION_ID` or the `az login` default. More than one operator (or a CI identity)? Put an Entra group with all of them in `key_vault_secrets_officer_principal_ids`: only identities with Secrets Officer on the vault can refresh its secrets, so anyone else's plan/apply/destroy fails with 403 |
| Terraform ≥ 1.9 | <https://developer.hashicorp.com/terraform/install> |
| HCP Terraform workspace | One workspace per environment, **execution mode Local** (HCP only stores the state). `export TF_CLOUD_ORGANIZATION=<org> TF_WORKSPACE=<workspace>`, then `terraform login` (or `TF_TOKEN_app_terraform_io`) |
| Docker Hub images | Published by GitHub Actions (`ci.yml` for `latest`, `release.yml` for `X.Y.Z`); no local Docker needed |
| Neon account + API key | Neon console → Account settings → API keys; `export NEON_API_KEY=...`. If your Neon account uses organizations (Neon answers `org_id is required`) also `export TF_VAR_neon_org_id=org-...` from the organization settings (an account identifier, deliberately not committed) |
| SMTP relay | Any provider with SMTP credentials and a verified sender address |
| Environment file | `infra/azure/terraform/environments/<env>.tfvars` is committed and holds every non-secret input (shared with `deploy-azure.yml`): set `image_namespace` and the `smtp_*` placeholders before the first deploy |
| Secrets file | `cp infra/azure/terraform/terraform.tfvars.example infra/azure/terraform/terraform.tfvars` (gitignored): `smtp_password`, optionally `dockerhub_*`; or `TF_VAR_*` environment variables |

Private Docker Hub repositories additionally need `dockerhub_username` + a **read-only** access
token in `dockerhub_token`; with public repositories leave both empty.

## Deploy

```bash
az login && terraform login
export TF_CLOUD_ORGANIZATION=<org> TF_WORKSPACE=<workspace> NEON_API_KEY=<key>
terraform -chdir=infra/azure/terraform init
terraform -chdir=infra/azure/terraform apply -var-file=environments/prod.tfvars -var release_version=0.3.1
terraform -chdir=infra/azure/terraform apply -var-file=environments/prod.tfvars -var release_version=0.3.1 -var keycloak_version=0.3.0
terraform -chdir=infra/azure/terraform apply -var-file=environments/prod.tfvars -var release_version=latest   # CI images
terraform -chdir=infra/azure/terraform plan  -var-file=environments/prod.tfvars -var release_version=0.3.1    # preview
```

The environment file is the same one `deploy-azure.yml` loads, so a local and a CI apply use identical
non-secret inputs. `release_version` has no default (it must be explicit, so an apply never rolls
to `latest` by accident); each component's image is chosen
independently: `api_version`, `web_version`, `keycloak_version` override it. `X.Y.Z` deploys `<ns>/birrapoint-<component>-release:X.Y.Z`, `latest` deploys
`<ns>/birrapoint-<component>:latest`.

Terraform owns the running image (ADR-0022): changing a version and applying creates a new
revision of that app; rollback is an apply with the previous version. **`latest` on an existing
environment does not re-pull**, because the template does not change; use release versions to
update. The API migrates the database on startup. A local apply has no revision health gate;
`deploy-azure.yml` adds one after its apply (`.github/scripts/wait-revisions.sh`), so check the
revisions yourself (`az containerapp revision list`) after a local rollout.

Environment guard: the HCP workspace (`TF_WORKSPACE`) must hold the environment you deploy or
tear down. `deploy-azure.yml` and `teardown.ps1` read `terraform output -raw environment` after `init`
and stop when it differs (an empty state is allowed).

On a first apply Terraform waits ~90 s after granting `Key Vault Secrets Officer`, for the role to
reach the vault, before writing the secrets. The apps' own role assignments can only be created
once the apps (and so their identities) exist, so a brand-new app's first revision may start
before it can read its secrets. Terraform then waits another ~120 s for those roles to propagate.
The API retries refused reads for ~2.5 min and Keycloak's loader for 180 s. A brand-new
environment's first apply therefore takes several minutes longer.

Outputs: `web_url`, `keycloak_url`.

### Tests

`terraform -chdir=infra/azure/terraform test` (mocked providers, no credentials) covers image resolution,
version validation, naming and the absence of a deploy-client secret. Teardown logic is covered by
Pester (`infra/azure/tests`, Pester 5+; Windows PowerShell 5.1 ships 3.4, which cannot run them):

```powershell
Install-Module Pester -MinimumVersion 5.0 -Scope CurrentUser -SkipPublisherCheck   # once
Invoke-Pester infra/azure/tests
```

Both run in `infra.yml` when `infra/**` changes. `deploy-azure.yml` runs the same `init` + `apply` with
the release versions through Azure OIDC; its one-time setup (HCP token, Azure identity, secrets,
`azure-production` environment) is in [`infra/azure/github-actions-setup.md`](../github-actions-setup.md).

First start takes a few minutes: Keycloak creates its schema on Neon and imports the realm, and
the API applies EF Core migrations (including the BJCP catalog seed) before serving.

The Keycloak admin console is at `<keycloak_url>/admin`, user `admin`, password from
`terraform -chdir=infra/azure/terraform output -raw keycloak_admin_password`.

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
   `az containerapp revision restart` for `birrapoint-prod-api` and `birrapoint-prod-kc`.

PITR only works while the Neon project exists: `teardown.ps1` deletes it, so nothing can be
restored afterwards.

For a non-destructive check first, create a branch from a past timestamp (Neon console →
Branches → New branch → "Past data") and inspect it with `psql` before restoring `main`.

## Tear down

```powershell
$env:NEON_API_KEY = "<key>"   # plus TF_CLOUD_ORGANIZATION, TF_WORKSPACE and a Terraform login
./infra/azure/teardown.ps1 -WhatIf  # preview, changes nothing
./infra/azure/teardown.ps1          # wipes everything; asks to type the resource group name (-Force skips it)
```

Always a full wipe, **data included**; the next deploy starts from scratch (ADR-0022):

1. `terraform init` against the HCP workspace and a guard (the workspace must hold the requested
   environment or be empty, otherwise nothing is changed), then, after the confirmation,
   `terraform destroy` of everything Terraform
   manages: Container Apps, environment, Log Analytics, Key Vault (purged), role assignments and the
   Neon project;
2. a sweep of whatever remains, found by name: Log Analytics purge and `az group delete` of
   `birrapoint-<env>-rg`, purge of the soft-deleted Key Vault, deletion of the Neon project whose
   id is in the Terraform state (`terraform output neon_project_id`), after the Neon API confirms
   its name is exactly `birrapoint-<env>-neon`. Neon is never selected by name alone, because the
   account is shared with the AWS deployment (`birrapoint-<env>-aws-neon`): with an empty state the
   script only reports name matches, and `-NeonProjectId <id>` deletes one (its name must still match);
3. removal of the local `infra/azure/terraform/.terraform` and a final verification.

Pass `-Environment` for an environment other than `PROD` (its `environments/<env>.tfvars` is used), `-NeonOrgId` for the Neon organization (default:
`TF_VAR_neon_org_id`, then the API key's only organization). Docker Hub images, GitHub secrets and the HCP workspace itself are not
touched. The script is idempotent: re-run it after a partial failure. Log Analytics and Key Vault
are purged, not soft-deleted (`permanently_delete_on_destroy`, `purge_soft_delete_on_destroy`), so
a redeploy never collides with them (the vault's name is globally unique). The same provider
setting purges Log Analytics immediately if Terraform ever *replaces* the workspace during a
normal apply.

The realm is imported on the first start of a brand-new environment with the right `SPA_URL`, so
no Keycloak client needs fixing after a redeploy.

## State from before ADR-0022

Environments deployed with `deploy.ps1` keep their state in an Azure Storage account
(`birrapoint-<env>-tfstate-rg`). Either remove them with the previous version of `teardown.ps1`
(`git show <old-commit>:infra/teardown.ps1`) and deploy fresh, or move the state with
`terraform init -migrate-state` after pointing a local `backend "azurerm"` block at it.

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
  new API revision together, and both may run the DispatchJob worker. Jobs are claimed
  atomically with a lease (ADR-0024), so a job in flight is not run twice; if the old revision is
  stopped mid-job, the new one recovers it once the lease (default 2 min) expires.
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
