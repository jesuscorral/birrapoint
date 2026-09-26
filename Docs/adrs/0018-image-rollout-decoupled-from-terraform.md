# 0018 - Image rollout decoupled from Terraform (`az containerapp update`, `deploy.ps1 -AppsOnly`)

**Status:** Accepted
**Date:** 2026-09-26

## Context

FR-064 introduces three GitHub Actions pipelines. The integration pipeline (`ci.yml`) publishes
`<ns>/birrapoint-<component>:latest` and the release pipeline publishes immutable
`<ns>/birrapoint-<component>-release:X.Y.Z`. The deploy pipeline then has to put a chosen version
into production.

Under ADR-0016, `infra/deploy.ps1` did everything in one pass. It built and pushed the images,
tagged them with the commit SHA, and ran `terraform apply` with an `image_tag` variable, so the
running image was a Terraform-managed attribute. Using that path for every application release
would mean:

- **Too much privilege.** The deploy pipeline would need the infrastructure credentials:
  Contributor on the subscription, the Terraform state, `NEON_API_KEY` and the full
  `terraform.tfvars` with its SMTP, Keycloak and Docker Hub secrets.
- **Unrelated changes.** A version deploy would also apply any pending infrastructure change.
- **Rollbacks.** A later `terraform apply` for an infrastructure change would roll every app back
  to whatever image its variables held.
- **Coarse versioning.** One tag for all three images, although they are released independently.

## Decision

1. **Terraform owns the infrastructure, not the running image.** The three Container Apps take
   full image references (`api_image`, `web_image`, `keycloak_image`, replacing `image_tag`),
   used only when an app is first created. `lifecycle.ignore_changes` covers the container image
   and `revision_suffix`.
2. **Images are rolled out with `az containerapp update --image`.** A unique revision suffix
   forces a new revision, which is the only way to make Container Apps re-pull a moved `latest`.
   The order is Keycloak, then the API (which migrates the database on startup), then the web
   app. Each new revision must be healthy before the next app starts. A pinned release an app
   already runs is skipped, so re-runs are idempotent.
3. **`deploy.ps1` resolves each image independently.** `-ApiVersion`, `-WebVersion` and
   `-KeycloakVersion` accept `X.Y.Z`, which selects the `-release` repository. When a version is
   omitted, the script deploys `latest` from the integration repository. It checks every image on
   the Docker Hub API before changing anything. It no longer builds or pushes images, and no
   longer requires Docker.
4. **`deploy.ps1 -AppsOnly`** runs only the rollout, with no Terraform, tfvars, Neon key or
   state. The deploy pipeline (T136) uses this mode with an identity scoped to the application
   resource group.
5. **Testable logic lives in a separate module.** Image resolution and the rollout decisions are
   in `infra/DeployImages.psm1`, unit-tested with Pester (`infra/tests/`). CI runs these tests,
   together with `terraform fmt` and `terraform validate`, in the `infra` job.

## Consequences

- **Less privilege for releases.** The deploy pipeline needs only `az` rights on
  `rg-<name_prefix>`. Infrastructure changes remain a deliberate, credentialed `deploy.ps1` run.
- **Safe infrastructure applies.** A `terraform apply` can no longer roll an app back. As a
  consequence, Terraform state does not record which version is live; the Container App itself
  is the source of truth (`az containerapp show`, the revision list).
- **Independent versions.** Each component can run a different version, and rollback means
  re-running the rollout with the previous versions.
- **First-time creation still uses Terraform.** A new environment gets its first images from a
  full `deploy.ps1` run; after that, the rollout owns the image.
- **ADR-0016 still applies** except for its build/push step and the `image_tag` variable,
  which this decision supersedes.
