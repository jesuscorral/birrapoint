# 0022 - Terraform-only deployment with state in HCP Terraform

**Status:** Accepted — supersedes ADR-0018 and ADR-0020, and the deploy-script part of ADR-0016
**Date:** 2026-10-03

## Context

ADR-0016/0018 split deployment in two: `infra/deploy.ps1` bootstrapped an Azure Storage state
backend and ran `terraform apply`, then rolled images out with `az containerapp update` while
Terraform ignored the image (`ignore_changes`, per-apply `revision_suffix`). ADR-0020 added a
Keycloak service-account client so the script could fix the SPA redirect URIs when a kept realm met
a new web domain. The result: three PowerShell modules with their own Pester tests, two sources of
truth for the running image, a powerful Keycloak client, and a teardown mode that kept data and
state. The environment is disposable, so none of that continuity is needed.

## Decision

1. **Terraform is the only deploy tool.** Deploying is `terraform init` + `terraform apply`
   (workstation or `deploy.yml`). `deploy.ps1`, `DeployImages`, `KeycloakClient`, `ResourceNames`
   and their tests are removed.
2. **Images are plain variables**: `image_namespace`, `release_version` (`latest` or `X.Y.Z`) and
   optional `api_version`/`web_version`/`keycloak_version`. No `ignore_changes`, no
   `revision_suffix`; a changed version creates a new revision.
3. **State in HCP Terraform** (`cloud {}`; organization and workspace from `TF_CLOUD_ORGANIZATION`
   and `TF_WORKSPACE`, token from `terraform login` or `TF_TOKEN_app_terraform_io`). The workspace
   uses **Local execution**: HCP only stores state, plans run with the operator's `az login` or the
   pipeline's OIDC identity. No state resource group or storage account.
4. **Every deployment starts from scratch**: the realm is imported with the right `SPA_URL`, so the
   `birrapoint-deploy` client, its secret and the redirect-URI sync are removed.
5. **`infra/teardown.ps1` always wipes everything** (destroy incl. Neon, sweep by name, Key Vault
   and Log Analytics purge, local `.terraform`). No data is kept.
6. `terraform test` (mocked providers, `infra/terraform/tests/`) runs in `infra.yml`.
7. **Committed, non-secret environment files** (`infra/terraform/environments/<env>.tfvars`) hold
   every non-secret input and are loaded by both local applies and `deploy.yml`, so the two never
   diverge. Secrets stay in the gitignored `terraform.tfvars` or `TF_VAR_*`. `image_namespace`
   ships as `CHANGE-ME` and is rejected by a validation until set.
8. **`release_version` is required** (no default); `latest` is accepted only when passed explicitly.
9. **Environment/workspace guard**: `deploy.yml` and `teardown.ps1` read
   `terraform output -raw environment` after `init` and refuse to continue when the HCP workspace
   holds a different environment (empty state allowed).
10. **CI revision health gate**: after the apply, `deploy.yml` runs
    `.github/scripts/wait-revisions.sh` (up to 10 min): latest revision ready, not Failed, Degraded
    tolerated briefly, Healthy (or scaled to zero with `minReplicas` 0). Local applies have no such
    gate.

## Consequences

- One mechanism and one source of truth for the running image; far less custom code.
- `deploy.yml` now runs the whole apply: its identity needs Contributor + User Access
  Administrator, plus `NEON_API_KEY`, `TF_API_TOKEN` and the SMTP settings. It is broader than the
  old resource-group-scoped rollout identity; the `production` approval and main-only rule remain.
- `latest` does not re-pull on an existing environment (the template is unchanged); updates use
  release versions.
- A rollout can no longer be done without Terraform credentials; rollback is an apply with the
  previous version.
- Data survives only inside the Neon PITR window while the project exists; teardown deletes the
  project, so there is no restore after it.
- Dependency on an external service (HCP Terraform) for state; the free tier suffices.
- Old environments with a Storage-backed state must be torn down by hand or migrated with
  `terraform init -migrate-state`.
