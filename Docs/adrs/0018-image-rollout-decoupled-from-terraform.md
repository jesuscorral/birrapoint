# 0018 - Image rollout decoupled from Terraform (`az containerapp update`, `deploy.ps1 -AppsOnly`)

**Status:** Accepted
**Date:** 2026-09-26

## Context

FR-064 adds CI (publishes `<ns>/birrapoint-<c>:latest`), release (publishes
`<ns>/birrapoint-<c>-release:X.Y.Z`) and deploy pipelines. Under ADR-0016 the running image was a
Terraform variable (`image_tag`), so every application release would need full infrastructure
credentials (subscription rights, state, `NEON_API_KEY`, all tfvars secrets), would apply unrelated
infrastructure changes, could be rolled back by a later infrastructure apply, and forced one tag on
three independently released images.

## Decision

1. **Terraform owns infrastructure, not the running image.** Apps take `api_image`, `web_image`,
   `keycloak_image`, used only at creation (`lifecycle.ignore_changes` on the image). Each apply gets
   a fresh `revision_suffix` (`infra-<UTC timestamp>`) from `deploy.ps1`, because Container Apps
   rejects reusing a suffix; so every full apply restarts all three apps.
2. **Rollout with `az containerapp update --image`** and a unique suffix (forces re-pulling a moved
   `latest`), in order Keycloak → API (migrates on startup) → web. Each new revision must become
   healthy (web: provisioned, it may scale to zero) before the next app; failures stop the deploy.
   An app is skipped only when its serving revision already runs the target, so re-runs are
   idempotent and failed rollouts retried.
3. **Per-component versions**: `-ApiVersion`/`-WebVersion`/`-KeycloakVersion X.Y.Z` select the
   release repository; omitted means `latest`. Every image is checked on Docker Hub first.
   `deploy.ps1` no longer builds or pushes images.
4. **`deploy.ps1 -AppsOnly`** runs only the rollout (no Terraform, tfvars, Neon key or state); the
   deploy pipeline uses it with an identity scoped to `birrapoint-<env>-rg`.
5. **Testable logic** in `infra/DeployImages.psm1` with Pester tests, run by `infra.yml` together
   with `terraform fmt`/`validate`. `ci.yml` never touches Terraform or deploys.

## Consequences

- Releases need only `az` rights on the application resource group; infrastructure changes remain a
  deliberate, credentialed full run.
- An infrastructure apply can no longer roll an app back. Terraform state does not know the live
  version; the Container App (revision list) is the source of truth.
- Components can run different versions; rollback = rollout of the previous versions.
- Terraform must run through `deploy.ps1` (`revision_suffix` has no default).
- A new environment still gets its first images from a full run.
- Revision-suffix behavior, health semantics and "rollout then env-var change" are validated against
  Azure in T137.
