# CI setup shared by both clouds (FR-064)

The one-time GitHub configuration that does not depend on the cloud: Docker Hub, the image
namespace and the release bot. Cloud-specific setup (identity, environment, Terraform inputs)
lives next to each deployment:

- Azure: [`azure/github-actions-setup.md`](azure/github-actions-setup.md), runbook [`azure/deployment-runbook.md`](azure/deployment-runbook.md)
- AWS: [`aws/github-actions-setup.md`](aws/github-actions-setup.md), runbook [`aws/deployment-runbook.md`](aws/deployment-runbook.md)

A deployment targets one cloud; each has its own independent deploy workflow, GitHub environment,
HCP Terraform workspace and `environments/<env>.tfvars`. Nothing below is stored in the repository.

| Workflow | Trigger | Does | Needs |
|---|---|---|---|
| `ci.yml` | PRs and pushes to `main` | Quality gates; on `main`, pushes `<ns>/birrapoint-*:latest` + `:sha-<short>` | Docker Hub |
| `infra.yml` | Changes to `infra/**` | `terraform fmt`/`validate`/`test` (no backend) and Pester (teardown), per cloud | nothing |
| `workflows.yml` | Changes to `.github/**` | actionlint, release script tests | nothing |
| `release.yml` | Manual | Publishes `<ns>/birrapoint-*-release:X.Y.Z`, tag, GitHub Release, bumps `version.txt` | Docker Hub |
| `deploy-azure.yml` | Manual, approval | Applies the Azure Terraform root with a release version (ADR-0022), then waits for healthy revisions | Azure OIDC, HCP Terraform, Neon, SMTP, `azure-production` environment |
| `deploy-aws.yml` | Manual, approval | Applies the AWS Terraform root with a release version (ADR-0022, ADR-0023), then waits for stable ECS services | AWS OIDC role, HCP Terraform, Neon, SMTP, `aws-production` environment |

Commands are PowerShell, run from the repository root after `gh auth login`.

## 1. Docker Hub (CI, release and deploys)

1. Create a Docker Hub **personal access token** with **Read & Write** scope: Docker Hub →
   Account settings → Personal access tokens.
2. Store it:

```powershell
gh secret set DOCKERHUB_USERNAME --body "<docker hub user>"
gh secret set DOCKERHUB_TOKEN            # paste the token when prompted
gh variable set IMAGE_NAMESPACE --body "<docker hub user or org>"

# Optional, only needed for private repositories: a separate READ-ONLY token for the deploy
# workflows, which never receive the read/write token above
gh secret set DOCKERHUB_READ_TOKEN
```

`IMAGE_NAMESPACE` is used by `ci.yml` and `release.yml` only (`deploy-azure.yml` and `deploy-aws.yml`
read the namespace from `infra/<cloud>/terraform/environments/<env>.tfvars`; keep them equal). It must be a **variable**, not a
secret. The workflows read `vars.IMAGE_NAMESPACE`,
and a secret would also mask every image name in the logs as `***`. The six repositories
(`birrapoint-{api,web,keycloak}` and `birrapoint-{api,web,keycloak}-release`) are created by the
first push.

## 2. The release bot and `main`

`release.yml` pushes the `vX.Y.Z` tag and the `version.txt` bump with the workflow's own
`GITHUB_TOKEN` (`contents: write`). This works as long as `main` is **not protected**, which is
the case today.

If you protect `main` with a ruleset that requires pull requests, `GITHUB_TOKEN` can no longer
push the bump. In that case:

1. Create a GitHub App with *Contents: Read & write*, install it on the repository, and add it to
   the ruleset's **bypass list**.
2. In `release.yml`'s `finalize` job, mint a token with `actions/create-github-app-token`
   (pinned by SHA), and use it for the checkout and pushes instead of `GITHUB_TOKEN`.

A **tag ruleset** protecting `v*` tags would block the bot's tag push in the same way; the same
GitHub App bypass applies.

If CI later becomes a required check, remember that docs-only PRs never produce it, because of
the path filters (T137). The reusable gates report as `gates / backend` and `gates / frontend`.

`release.yml`, `deploy-azure.yml` and `deploy-aws.yml` refuse to run unless dispatched from `main` ("Use workflow from:
main" in the Actions tab, the default).
