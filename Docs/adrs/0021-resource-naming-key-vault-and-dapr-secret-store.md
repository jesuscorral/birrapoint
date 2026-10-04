# 0021 - Resource naming convention, Key Vault with managed identities and the Dapr secret store

**Status:** Accepted — the `tfstate-rg` state naming and `ResourceNames.psm1` no longer apply
(state is in HCP Terraform, ADR-0022); the resource naming rule itself stands; paths below
moved to `infra/azure/` (ADR-0023)
**Date:** 2026-09-27

## Context

User request (2026-09-27): resource names from a free-form prefix said nothing about the
environment; application secrets were Container Apps secrets exposed as environment variables,
readable by anyone able to read the app definition (including the deploy identity), with no central
audit or rotation point; the API read them as ordinary settings.

## Decision

1. **Naming `birrapoint-<environment>-<acronym>`**: Terraform variable `environment` (default
   `PROD`, lower-cased in names). Acronyms `rg`, `log`, `cae`, `kv`, `neon`; the three Container Apps
   use `api`, `web`, `kc`. State: `birrapoint-<env>-tfstate-rg`, storage account
   `birrapoint<env>st<subscription>`, blob `birrapoint-<env>.tfstate` — one state per environment.
   Rule in `infra/ResourceNames.psm1`; both scripts take `-Environment`.
2. **Key Vault `birrapoint-<env>-kv` holds every app secret** (Azure RBAC, written by Terraform).
   `Key Vault Secrets Officer` for the Terraform identity and
   `key_vault_secrets_officer_principal_ids` (90 s propagation wait on first apply). No purge
   protection, 7-day soft delete, purged on destroy; `key_vault_name` overrides the globally unique
   name.
3. **System-assigned managed identity per Container App, `Key Vault Secrets User` per secret** for
   the app that uses it (not vault-wide: Dapr scopes do not restrict secrets, and a vault-wide grant
   would let the externally exposed Keycloak read the API's database credentials). The web app has
   no secrets and no grant.
4. **Apps read secrets through Dapr**: component `secretstore` (`secretstores.azure.keyvault`,
   authenticates with the caller's identity) scoped to the API and Keycloak.
   - **API**: `Common/Secrets/DaprSecrets.cs` (`Dapr.Extensions.Configuration`) loads an explicit
     list when `Dapr:SecretStore` is set; `--` maps to `:` (`ConnectionStrings--db` →
     `ConnectionStrings:db`), so consumers are unchanged. Locally nothing changes.
   - **Keycloak** (third-party Java image): the entrypoint runs a small Java program that reads the
     `DAPR_SECRETS` mappings from the sidecar (retry ≤ 180 s), exports them and `exec`s `kc.sh`.
     Tested by `DaprSecretsEnv.test.sh`.
5. **Exception**: the Docker Hub pull token stays a Container Apps registry secret (needed before
   any container starts).

Container Apps Key Vault references were rejected: a system-assigned identity exists only after the
app, but the app cannot be created while referencing a vault it cannot read yet (two-phase apply or
user-assigned identities, both ruled out).

## Consequences

- Secrets are no longer readable from app definitions; direct reads need a Key Vault role and are
  auditable. This is **not** a boundary against a resource-group Contributor (it can deploy any
  image that runs with the app's identity, or exec into Keycloak), so the deploy identity stays
  behind the protected `production` environment.
- A full `deploy.ps1` run needs Owner (or Contributor + User Access Administrator) to create role
  assignments; `-AppsOnly` is unchanged.
- First start of a new app can race RBAC propagation: Terraform waits 120 s after app
  assignments, the API retries (15 × 10 s), Keycloak's loader retries for 180 s.
- Secrets are read at startup; a rotation applies on the next revision or restart.
- New dependencies: `Dapr.Extensions.Configuration`, the `hashicorp/time` provider, a Dapr sidecar
  per secret-reading app (constitution v1.4.0).
- Key Vault is reached over its public endpoint (no VNet yet).
- Renaming replaces existing resources; migration procedure in `infra/terraform/README.md`.
- Not yet validated against a real subscription (T099).
