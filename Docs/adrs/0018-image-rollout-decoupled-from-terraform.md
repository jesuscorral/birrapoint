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
   used only when an app is first created; `lifecycle.ignore_changes` covers the container
   image only. Each apply also receives a fresh `revision_suffix` (`infra-<UTC timestamp>`,
   passed by `deploy.ps1`): a rollout leaves its own suffix in the Terraform state, and Container
   Apps rejects a template change that would reuse an existing suffix, so ignoring the suffix
   would make the next infrastructure change fail. The cost is that every full apply creates a
   new revision of all three apps (a restart); full applies are rare and deliberate.
2. **Images are rolled out with `az containerapp update --image`.** A unique revision suffix
   forces a new revision, which is the only way to make Container Apps re-pull a moved `latest`.
   The order is Keycloak, then the API (which migrates the database on startup), then the web
   app. The script polls the revision it created until it is healthy — or, for the web app,
   which may scale to zero, provisioned with no replicas — before the next app starts; failed,
   degraded or deprovisioned revisions stop the deployment. The skip decision looks at what is
   actually serving, not at the app template: in Single revision mode a failed rollout leaves
   the new image in the template while the previous revision keeps serving. An app is skipped
   only when its latest revision is the ready one and runs the target release (or `latest`
   that the preceding apply just pulled), so re-runs are idempotent and a failed rollout is
   retried.
3. **`deploy.ps1` resolves each image independently.** `-ApiVersion`, `-WebVersion` and
   `-KeycloakVersion` accept `X.Y.Z`, which selects the `-release` repository. When a version is
   omitted, the script deploys `latest` from the integration repository. It checks every image on
   the Docker Hub API before changing anything. It no longer builds or pushes images, and no
   longer requires Docker.
4. **`deploy.ps1 -AppsOnly`** runs only the rollout, with no Terraform, tfvars, Neon key or
   state. The deploy pipeline (T136) uses this mode with an identity scoped to the application
   resource group.
5. **Testable logic lives in a separate module.** Image resolution and the rollout decisions are
   in `infra/DeployImages.psm1`, unit-tested with Pester (`infra/tests/`). The `infra.yml`
   workflow runs these tests together with `terraform fmt` and `terraform validate`, only when
   `infra/**` changes; the integration pipeline (`ci.yml`) contains nothing Terraform-related and
   never deploys.

## Consequences

- **Less privilege for releases.** The deploy pipeline needs only `az` rights on
  `rg-<name_prefix>`. Infrastructure changes remain a deliberate, credentialed `deploy.ps1` run.
- **Safe infrastructure applies.** A `terraform apply` can no longer roll an app back. As a
  consequence, Terraform state does not record which version is live; the Container App itself
  is the source of truth (`az containerapp show`, the revision list).
- **Independent versions.** Each component can run a different version, and rollback means
  re-running the rollout with the previous versions.
- **Terraform must only run through `deploy.ps1`,** which supplies the per-apply revision
  suffix; `revision_suffix` has no default, so a bare `terraform apply` asks for it.
- **Not yet verified against Azure:** the revision-suffix behaviour, the health semantics without
  probes and with scale-to-zero, and "roll out, then change an env var through Terraform" are
  validated in T137.
- **First-time creation still uses Terraform.** A new environment gets its first images from a
  full `deploy.ps1` run; after that, the rollout owns the image.
- **ADR-0016 still applies** except for its build/push step and the `image_tag` variable,
  which this decision supersedes.
