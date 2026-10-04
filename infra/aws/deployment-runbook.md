# AWS deployment runbook: from zero to a running environment

This is the end-to-end checklist for deploying BirraPoint to **AWS** (ECS Fargate, ALB, CloudFront,
Secrets Manager, Neon) from scratch (ADR-0022, ADR-0023). It lists every account, key and setting
needed, in the order to create them. The details behind each step are in
[`terraform/README.md`](terraform/README.md) (topology, local apply, known risks) and
[`github-actions-setup.md`](github-actions-setup.md) (CI). The Docker Hub, release-bot and workflow
overview shared by both clouds is in [`../ci-setup.md`](../ci-setup.md). The Azure equivalent is
[`../azure/deployment-runbook.md`](../azure/deployment-runbook.md); the two deployments are
independent (own Terraform root, HCP workspace, GitHub environment and teardown).

There are two phases:

- **Phase A**: the first deploy, from your workstation with your own AWS credentials.
- **Phase B**: every later deploy, through `deploy-aws.yml` in GitHub Actions.

Both phases run the same `terraform apply` against the same HCP Terraform state, with the same
committed `infra/aws/terraform/environments/prod.tfvars`.

## 1. Accounts and credentials

| # | Service | What to create | Used by | Where it goes |
|---|---|---|---|---|
| 1 | **AWS** | An account **dedicated to BirraPoint** (one environment per account and region) and an administrator identity | Terraform (all resources); teardown | `aws configure sso` / `aws sso login`, or access keys of an admin IAM user; `AWS_PROFILE` / `AWS_*` |
| 2 | **HCP Terraform** (free) | Organization + a workspace **for the AWS root** (not the Azure one), execution mode **Local** | Terraform state | `TF_CLOUD_ORGANIZATION`, `TF_WORKSPACE` |
| 3 | HCP Terraform | **User token** (via `terraform login`) for your workstation | Local apply / teardown | `%APPDATA%\terraform.d\credentials.tfrc.json` (written by `terraform login`) |
| 4 | HCP Terraform | **Team token** for a team that can only access this workspace | `deploy-aws.yml` | GitHub environment secret `TF_API_TOKEN` |
| 5 | **Neon** | Account + **API key** | Terraform (Neon project, roles, databases); teardown | `NEON_API_KEY` env var; GitHub environment secret `NEON_API_KEY` |
| 6 | Neon | Organization id (`org-...`), needed when your account uses organizations (Neon answers `org_id is required`) | Terraform; teardown `-NeonOrgId` | `TF_VAR_neon_org_id`; GitHub variable `NEON_ORG_ID` |
| 7 | **Docker Hub** | Account (user or organization = image namespace) | Image hosting | `image_namespace` in `prod.tfvars`; GitHub variable `IMAGE_NAMESPACE` |
| 8 | Docker Hub | **Personal access token, Read & Write** | `ci.yml`, `release.yml` push images | GitHub secrets `DOCKERHUB_USERNAME`, `DOCKERHUB_TOKEN` |
| 9 | Docker Hub | *Optional:* **Read-only** access token (private repos, or to avoid pull rate limits) | ECS image pull (`repositoryCredentials`); `deploy-aws.yml` image check | `dockerhub_token` in `terraform.tfvars`; GitHub secret `DOCKERHUB_READ_TOKEN` |
| 10 | **SMTP provider** | SMTP credentials + a **verified sender address** | API emails (judge invitations, results), Keycloak mail | `smtp_*` in `prod.tfvars`; password in `terraform.tfvars` / GitHub secret `SMTP_PASSWORD` |
| 11 | **AWS IAM** | OIDC provider for GitHub + role `birrapoint-github-deploy` (OIDC, no stored key) | `deploy-aws.yml` | GitHub environment secret `AWS_ROLE_ARN` |
| 12 | **GitHub** | Repository admin; `aws-production` environment with required reviewers | `deploy-aws.yml` approval gate | Repository settings |

Terraform generates every other secret itself: the Keycloak admin password, the API admin-client
secret, the CloudFront origin-verification secrets and the Neon role passwords. It stores them in
Secrets Manager and in the HCP state.

> **Security:** the HCP state holds every generated production secret in plain text. Anyone with
> access to the workspace or a token for it can read them, so use a team token scoped to this
> workspace only for CI. Never commit any of the secrets above. `terraform.tfvars` is gitignored.

## 2. Workstation tools

| Tool | Version | Needed for |
|---|---|---|
| AWS CLI v2 (`aws`) | current | Sign-in, verification, health checks, teardown sweep |
| Terraform | 1.14.3 (same as `deploy-aws.yml`; `>= 1.9` works) | Apply / destroy |
| GitHub CLI (`gh`) | current | Secrets, variables, workflow runs |
| PowerShell 7 + Pester 5 | n/a | `teardown.ps1` and its tests (the script also runs on Windows PowerShell 5.1) |

No local Docker is needed: images are built and published by GitHub Actions.

## Phase A: first deploy from the workstation

### A1. Publish the images

`deploy-aws.yml` and Terraform only pull images; they never build them.

1. Create the Docker Hub token (item 8) and store it in GitHub:

   ```powershell
   gh secret set DOCKERHUB_USERNAME --body "<docker hub user>"
   gh secret set DOCKERHUB_TOKEN                 # paste when prompted
   gh variable set IMAGE_NAMESPACE --body "<docker hub user or org>"
   ```

2. Merge to `main`. `ci.yml` pushes `<ns>/birrapoint-{api,web,keycloak}:latest`.
3. Cut a release: `gh workflow run release.yml -f ref=main -f bump=patch`. This pushes
   `<ns>/birrapoint-*-release:X.Y.Z`. Note the `X.Y.Z` from the GitHub Release.

### A2. Create the HCP Terraform workspace

1. Sign up at <https://app.terraform.io>, create (or reuse) an organization.
2. Create a workspace for the AWS root (e.g. `birrapoint-aws-prod`) of type **CLI-driven workflow**.
3. Workspace, Settings, General, **Execution Mode: Local**, then save. If it stays on Remote, the
   apply runs on HCP's servers, which have no AWS credentials, and fails.

### A3. Neon and SMTP

1. Neon console, Account settings, **API keys**, create a key.
2. Copy the organization id (`org-...`) from the organization settings. Keys that belong to an
   organization need it (Neon answers `org_id is required` otherwise).
3. From your SMTP provider, collect the host, port (587 with STARTTLS), user name, password, and a
   verified sender address.

### A4. Fill the environment file (committed)

Edit `infra/aws/terraform/environments/prod.tfvars` and commit it through a PR:

```hcl
region            = "eu-central-1"
image_namespace   = "<docker hub user or org>"   # CHANGE-ME is rejected by a validation
smtp_host         = "<smtp host>"
smtp_port         = 587
smtp_username     = "<smtp user>"
smtp_from_address = "<verified sender>"
```

### A5. Local secrets (not committed)

```powershell
Copy-Item infra/aws/terraform/terraform.tfvars.example infra/aws/terraform/terraform.tfvars
# edit: smtp_password = "...", optionally dockerhub_username / dockerhub_token (read-only)
```

### A6. Apply

Sign in to AWS with an administrator identity, either SSO (recommended) or the access keys of an
admin IAM user:

```powershell
aws configure sso                       # once; name the profile, e.g. birrapoint-admin
aws sso login --profile birrapoint-admin
$env:AWS_PROFILE = "birrapoint-admin"
# alternative: $env:AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY (never commit or share them)
aws sts get-caller-identity             # confirm the account

terraform login                         # stores the HCP user token

$env:TF_CLOUD_ORGANIZATION = "<org>"
$env:TF_WORKSPACE          = "<aws workspace>"
$env:NEON_API_KEY          = "<neon api key>"
$env:TF_VAR_neon_org_id    = "org-..."            # Neon organization (organization settings)

terraform -chdir=infra/aws/terraform init
# PowerShell splits an unquoted -var-file=...tfvars at the dot: keep the quotes.
terraform -chdir=infra/aws/terraform plan  "-var-file=environments/prod.tfvars" "-var=release_version=X.Y.Z"
terraform -chdir=infra/aws/terraform apply "-var-file=environments/prod.tfvars" "-var=release_version=X.Y.Z"
```

The first apply takes **15-20 minutes**: each CloudFront distribution needs 5-10 minutes to deploy,
and Keycloak's first start creates its schema on Neon and imports the realm (health check grace
period 10 minutes).

### A7. Verify

```powershell
terraform -chdir=infra/aws/terraform output web_url
terraform -chdir=infra/aws/terraform output keycloak_url
terraform -chdir=infra/aws/terraform output -raw keycloak_admin_password   # Keycloak admin, user "admin"
aws ecs describe-services --cluster birrapoint-prod-ecs `
  --services birrapoint-prod-api birrapoint-prod-web birrapoint-prod-kc `
  --query "services[].{service:serviceName,desired:desiredCount,running:runningCount,events:events[0:3].message}"
aws ecs wait services-stable --cluster birrapoint-prod-ecs --services birrapoint-prod-api birrapoint-prod-web birrapoint-prod-kc
```

CloudFront may need up to ~15 minutes after the apply before the new `*.cloudfront.net` domain
answers everywhere; a `403` or DNS error right after the apply is usually that.

1. Open `web_url`, self-register an organizer from the login page (no seeded account exists in
   production), and send one judge invitation to check SMTP.
2. Log in to `<keycloak_url>/admin` and change the `admin` password (T130 hardening is pending).

**First-apply checks** (details in [`terraform/README.md`](terraform/README.md), known limitations):

- **HSTS**: `curl -sI <web_url>` shows `strict-transport-security: max-age=31536000` (added by the
  CloudFront response headers policy, because the API never sees the original scheme).
- **Keycloak behind the proxy**: logging in to the PWA redirects to `keycloak_url` and back over
  https. If the redirects use `http` or the wrong host, check `KC_HOSTNAME` and the `Forwarded:
  proto=https` header added by the Keycloak distribution.
- **daprd logs**: look in the `/birrapoint/prod/api` and `/birrapoint/prod/keycloak` log groups
  (`daprd` stream prefix) for placement or scheduler connection retries; if present, add the flags
  that disable them (`terraform/README.md`).
- **Cloud Map**: the web tasks reach the API at `api.birrapoint-prod.local`; the PWA loading data
  proves the private DNS name resolves (`aws servicediscovery list-namespaces`).
- **60 s edge timeout**: both origins use `origin_read_timeout = 60`; a very large `.xlsx` import
  longer than that returns a 504 from CloudFront (request a quota increase in Service Quotas).
- **Security group quota**: a first apply that fails with `RulesPerSecurityGroupLimitExceeded` needs
  a Service Quotas increase for "rules per security group" (the CloudFront managed prefix list
  counts one rule per entry).

## Phase B: later deploys through GitHub Actions

### B1. AWS deploy role (OIDC)

Follow [`github-actions-setup.md` §3](github-actions-setup.md#3-aws-identity-for-deploy-awsyml-oidc-no-stored-key).
It creates the IAM OIDC provider for GitHub and the role `birrapoint-github-deploy`, trusted only by
jobs of this repository running in the `aws-production` environment.

### B2. HCP team token

HCP Terraform, organization Settings, Teams: create a team (e.g. `github-deploy-aws`) with access to
the AWS workspace only, then create a **team token**. Team management may need a paid HCP plan. On
the free plan, fall back to an organization or user token and keep the security note in section 1
in mind.

### B3. GitHub `aws-production` environment, secrets and variables

Follow [`github-actions-setup.md` §4](github-actions-setup.md#4-github-aws-production-environment).
Here is everything `deploy-aws.yml` reads:

| Kind | Name | Value |
|---|---|---|
| env secret | `AWS_ROLE_ARN` | Role ARN from B1 |
| env secret | `TF_API_TOKEN` | HCP team token (B2) |
| env secret | `NEON_API_KEY` | Neon API key |
| env secret | `SMTP_PASSWORD` | SMTP password |
| secret | `DOCKERHUB_USERNAME` | Docker Hub user (already set in A1) |
| secret (optional) | `DOCKERHUB_READ_TOKEN` | Read-only Docker Hub token |
| env variable | `TF_CLOUD_ORGANIZATION`, `TF_WORKSPACE` | Same as A6 (the AWS workspace) |
| env variable | `AWS_REGION` | Same as `region` in `prod.tfvars` (default `eu-central-1`) |
| env variable (optional) | `NEON_ORG_ID` | Neon organization id |
| env variable (optional) | `BIRRAPOINT_ENVIRONMENT` | Default `PROD` |

Add **required reviewers** to the `aws-production` environment, and restrict it to `main`.

### B4. Deploy

```powershell
gh workflow run release.yml -f ref=main -f bump=patch      # new X.Y.Z
gh workflow run deploy-aws.yml -f version=X.Y.Z                 # approve in the Actions tab
gh workflow run deploy-aws.yml -f version=X.Y.Z -f keycloak_version=X.Y.W   # per-component override / rollback
```

The workflow validates the version and checks that the images exist on Docker Hub. It waits for
approval, then guards that the HCP workspace holds the same environment. It then applies and
waits until every ECS service is stable.

## Tear down (wipes everything, data included)

```powershell
$env:NEON_API_KEY = "<key>"   # plus TF_CLOUD_ORGANIZATION, TF_WORKSPACE, terraform login, aws sso login
./infra/aws/teardown.ps1 -WhatIf   # preview: lists what exists and what would be removed
./infra/aws/teardown.ps1           # type birrapoint-prod to confirm
```

The script runs `terraform destroy`, then sweeps what is left (found by exact name **and** the tags
`application=birrapoint`, `environment=<env>`, so nothing of another environment or application is
touched), deletes the Neon project and verifies through the tagging API that nothing remains.
Removing the CloudFront distributions takes about 15 minutes. Accounts, tokens, Docker Hub images,
GitHub secrets, the IAM OIDC provider and deploy role, and the HCP workspace survive a teardown. The
next deploy is just step A6 (or B4) again. The Azure environment has its own `infra/azure/teardown.ps1`.

## Checklist

- [ ] AWS account dedicated to BirraPoint, administrator access (SSO or admin keys)
- [ ] HCP organization + AWS workspace in **Local** mode; `terraform login` done
- [ ] Neon API key (+ org id if needed)
- [ ] Docker Hub account + Read & Write token in GitHub; `IMAGE_NAMESPACE` variable
- [ ] `latest` images (merge to `main`) and a release `X.Y.Z` (`release.yml`)
- [ ] SMTP credentials + verified sender
- [ ] `prod.tfvars` filled and merged (`region`, `image_namespace`, `smtp_*`)
- [ ] `terraform.tfvars` with `smtp_password`
- [ ] First local `init` + `apply`; app reachable, invitation email received
- [ ] First-apply checks: HSTS, Keycloak redirects, daprd logs, Cloud Map, 60 s timeout, SG quota
- [ ] Keycloak `admin` password changed
- [ ] IAM OIDC provider + role `birrapoint-github-deploy`
- [ ] HCP team token
- [ ] GitHub `aws-production` environment: secrets, variables, reviewers, `main`-only
- [ ] `deploy-aws.yml` run end to end
