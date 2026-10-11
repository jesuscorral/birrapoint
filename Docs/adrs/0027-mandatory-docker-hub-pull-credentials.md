# 0027 - Mandatory Docker Hub pull credentials in both clouds

**Status:** Accepted — amends ADR-0016 (optional Docker Hub credential)
**Date:** 2026-10-11

## Context

ADR-0016 made `dockerhub_username`/`dockerhub_token` optional (private repositories only).
Azure Container Apps and ECS Fargate pull from shared outbound IPs, so anonymous pulls of public
repositories can hit Docker Hub rate limits and make a deploy or a task restart fail (PR #46
review M6).

## Decision

1. `dockerhub_username` and `dockerhub_token` are **mandatory** in both Terraform roots
   (`infra/azure/terraform`, `infra/aws/terraform`): no default, validation rejects values that
   are empty after trimming. There is no `dckr_pat_` prefix check because not every token is
   confirmed to carry it.
2. Credentials are **always wired**, even for public repositories, so pulls are authenticated.
3. The token is `sensitive`. Azure: a Container App registry secret. AWS: a Secrets Manager secret
   readable only by the ECS execution role. Never an environment variable.
4. `deploy-azure.yml` and `deploy-aws.yml` fail in the `resolve` job, before the approval gate,
   if `DOCKERHUB_USERNAME` or `DOCKERHUB_READ_TOKEN` is missing. This intentionally duplicates
   the Terraform validation to fail early.
5. Deploys use a separate **read-only** token, never the read/write token used by CI to push.
6. The AWS `dapr-init` image comes from `public.ecr.aws` and carries no credentials.
7. AWS `moved` blocks map the pre-T131 `dockerhub[0]` secret and version to the singleton
   resources, so existing deployments keep their secret instead of recreating it.

## Consequences

- Existing `terraform.tfvars` files and GitHub secrets must add both values before the next
  apply or deploy; otherwise validation or the `resolve` job fails.
- Rate-limit failures of image pulls are removed as a first-apply risk.
- One more credential to create and rotate (read-only PAT), scoped to pull.
- The early workflow check and the Terraform validation must be kept in sync.
