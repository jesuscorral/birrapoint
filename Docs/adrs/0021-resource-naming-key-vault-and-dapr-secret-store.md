# 0021 - Resource naming convention, Key Vault with managed identities and the Dapr secret store

**Status:** Accepted
**Date:** 2026-09-27

## Context

Three things about the cloud deployment (ADR-0016, ADR-0018) needed to change, at the user's
request of 2026-09-27:

- **Names.** Resources were named `<acronym>-<prefix>` or `<prefix>-<component>` (`rg-birrapoint`,
  `cae-birrapoint`, `birrapoint-api`) from a free-form `name_prefix`. The names said nothing about
  the environment, and a second environment needed a different prefix chosen by hand.
- **Where secrets live.** The Neon connection strings, the Keycloak bootstrap admin password, the
  API admin-client and deploy-client secrets and the SMTP password were Container Apps secrets,
  exposed to the containers as environment variables. Anyone who could read the Container App
  definition (Contributor on the resource group, which includes the deploy pipeline's identity)
  could read them, and there was no central place to audit or rotate them.
- **How the apps read them.** The API read secrets from configuration like any other setting.

## Decision

1. **Naming: `birrapoint-<environment>-<resource acronym>`.** Terraform has an `environment`
   variable (default `PROD`, 2-10 letters/digits) that replaces `name_prefix`. It is lower-cased in
   every name, because Container Apps names only allow lowercase. The acronyms are `rg`, `log`,
   `cae`, `kv` and `neon`. The three Container Apps use their component instead: `api`, `web` and
   `kc`. They are all Container Apps, so the generic `ca` acronym could not tell them apart, and it
   would make their public host names longer. The Terraform state follows the same convention:
   `birrapoint-<env>-tfstate-rg`, storage account `birrapoint<env>st<subscription>` (hyphens are
   not allowed) and blob `birrapoint-<env>.tfstate`. So each environment has its own state.
   `infra/ResourceNames.psm1` holds the rule for `deploy.ps1`, `teardown.ps1` and (inline)
   `deploy.yml`. Both scripts take `-Environment`, and `deploy.ps1` always passes it to Terraform,
   so names and state location cannot drift apart.
2. **Key Vault holds every application secret.** Vault `birrapoint-<env>-kv` uses Azure RBAC, not
   access policies. Terraform writes the secrets into it; the identity running Terraform is given
   `Key Vault Secrets Officer`, and a `time_sleep` of 90 s lets that assignment reach the data
   plane on a first apply. The vault has no purge protection and a 7-day soft-delete retention.
   Its name is globally unique, so a destroy purges it (provider feature) and `teardown.ps1`
   purges a vault left soft-deleted by a direct resource-group delete.
3. **Every Container App has a system-assigned managed identity** with read-only
   `Key Vault Secrets User` on the vault. The web app has no secret today, but it gets the same
   access as requested, so it can adopt the store without an infrastructure change.
4. **Apps read secrets through Dapr, never as settings.** A Dapr secret store component
   (`secretstore`, `secretstores.azure.keyvault`) on the Container Apps environment points at the
   vault. It has no client id, so the sidecar authenticates with the calling app's
   system-assigned identity. It is scoped to the two apps that have secrets. Dapr is enabled on
   the API and on Keycloak, with no app port: the sidecar only serves the app's own calls.
   - **API**: `Common/Secrets/DaprSecrets.cs` adds the Dapr .NET configuration provider
     (`Dapr.Extensions.Configuration` 1.18) when `Dapr:SecretStore` is set. It loads an explicit
     list of secrets, never the whole store, because the vault also holds Keycloak's secrets. Key
     Vault names allow only alphanumerics and `-`, so `--` stands for the `:` separator
     (`ConnectionStrings--db` → `ConnectionStrings:db`). The secrets therefore land under their
     existing configuration keys and no consumer changes. Locally the setting is absent, and the
     Aspire AppHost keeps injecting plain settings without a sidecar.
   - **Keycloak** is a third-party Java image, so no Dapr SDK can be used. The image's entrypoint
     (`infra/keycloak/dapr-secrets/docker-entrypoint.sh`) runs a small standard-library Java
     program. It reads the `ENV_VAR=secret-name` pairs in `DAPR_SECRETS` from the sidecar's
     secrets API, retrying for up to 300 s, and prints shell-quoted `export` lines. The entrypoint
     evaluates them and `exec`s `kc.sh`. The program is compiled in a build stage, because the
     runtime image only has a JRE, and tested by `DaprSecretsEnv.test.sh` in `infra.yml`. Without
     `DAPR_SECRET_STORE` the image behaves exactly like stock Keycloak.
5. **One exception: the Docker Hub pull token stays a Container Apps registry secret.** The
   platform needs it to pull the image, before any container or sidecar exists.

**Why not Container Apps Key Vault references** (`secret { key_vault_secret_id, identity =
"System" }`)? They would give Keycloak its secrets without an entrypoint. But a system-assigned
identity exists only once the app does, and the app cannot be created while its secrets reference
a vault it has no role on yet. That would need a two-phase apply, or user-assigned identities,
which the request ruled out. Dapr reads secrets at runtime, so the role assignment only has to be
in place when a revision starts.

## Consequences

- **Secrets leave the Container App definition.** Contributor on the resource group no longer
  reveals them, which includes the `deploy.yml` identity. Reading them needs a Key Vault data-plane
  role, and access is auditable in one place.
- **Terraform needs more rights.** A full `deploy.ps1` run needs Owner, or Contributor plus User
  Access Administrator, because Terraform creates role assignments. The image-only pipeline
  (`-AppsOnly`) is unchanged: an image rollout keeps each app's identity and Dapr settings.
- **First start of a new app can race RBAC.** An app's role assignment can only be created after
  the app, so the first revision of a brand-new app may start before its identity can read the
  vault. Keycloak's loader retries for 300 s. The API fails startup and is restarted, and the
  rollout step of `deploy.ps1` replaces any revision that is not healthy. Existing apps are
  unaffected.
- **Secrets are read once at startup.** A rotation reaches an app on its next revision or restart
  (as before, a rotated Keycloak admin-client secret must also be changed in the realm).
- **New dependencies.** `Dapr.Extensions.Configuration` (plus `Dapr.Client` and gRPC, transitively)
  in the API; the `hashicorp/time` Terraform provider; a Dapr sidecar per secret-reading app. This
  is a stack change, recorded in constitution v1.4.0.
- **Key Vault is reached over its public endpoint.** Azure RBAC still guards it. The Container
  Apps environment has no virtual network, so there is no private endpoint yet.
- **Renaming replaces resources.** Existing deployments use the old names and state location.
  Moving them either recreates the Azure resources under the new names (new URLs; the Neon
  project is renamed in place and keeps its data) or starts clean. The procedure is in
  `infra/terraform/README.md` ("Migrating an environment deployed before ADR-0021"), and the
  `deploy.yml` identity must be granted rights on the new resource group.
- **Not yet validated against a real subscription**, like the rest of Phase 16 (T099).
