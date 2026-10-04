# GitHub Actions setup: AWS (FR-064, T146)

This page covers the one-time configuration behind the CI/CD pipeline of the **AWS** deployment
(`deploy-aws.yml`, GitHub environment `aws-production`; each cloud has its own independent deploy
target, the Azure one is in [`../azure/github-actions-setup.md`](../azure/github-actions-setup.md)).
Nothing in it is stored in the repository. The workflow overview and everything shared by both
clouds (Docker Hub, `IMAGE_NAMESPACE`, the release bot and `main` protection) is in
[`../ci-setup.md`](../ci-setup.md): do its section 1 first.

Only `deploy-aws.yml` creates or changes AWS infrastructure: it runs the same `terraform apply` as a
workstation (see `terraform/README.md`), so it can also create a brand-new environment. State lives
in HCP Terraform, in a **workspace of its own** for this root (execution mode **Local**: HCP only
stores the state). The job authenticates to AWS with GitHub's OIDC token: no access key is stored
anywhere.

For the full from-scratch order (accounts, keys, first local apply) see [`deployment-runbook.md`](deployment-runbook.md).

Commands below are PowerShell. Run them from the repository root, with the AWS CLI v2 signed in as
an administrator of the target account (`aws sso login`) and `gh auth login`.

## 1. Docker Hub (CI, release and deploy)

Shared by both clouds: follow [`../ci-setup.md` §1](../ci-setup.md#1-docker-hub-ci-release-and-deploys).
`DOCKERHUB_READ_TOKEN` (optional, private repositories) is read by `deploy-aws.yml`.

## 2. HCP Terraform, Neon and SMTP (state and apply inputs)

1. In HCP Terraform create a workspace for the AWS root (for example `birrapoint-aws-prod`, **not**
   the Azure one), type **CLI-driven workflow**, execution mode **Local**, and a team/user API token.
2. Store the settings. Values marked *secret* go to the `aws-production` environment (step 4).

| Kind | Name | Value |
|---|---|---|
| secret | `TF_API_TOKEN` | HCP Terraform API token |
| secret | `NEON_API_KEY` | Neon API key |
| secret | `SMTP_PASSWORD` | SMTP relay password |
| secret (repository, optional) | `DOCKERHUB_READ_TOKEN` | read-only Docker Hub token |
| variable | `TF_CLOUD_ORGANIZATION`, `TF_WORKSPACE` | HCP organization and the **AWS** workspace |
| variable | `AWS_REGION` | region of the deployment, default `eu-central-1`; must match `region` in `environments/<env>.tfvars` |
| variable (optional) | `NEON_ORG_ID` | Neon organization id (not committed); needed when the account uses organizations |
| variable (optional) | `BIRRAPOINT_ENVIRONMENT` | environment name, default `PROD`; selects `terraform/environments/<env>.tfvars` |

Set the variables on the **environment** (step 4), not on the repository: a repository-level
`TF_WORKSPACE` already exists when the Azure deployment is configured, and an environment variable
overrides it only for jobs that run in that environment.

Non-secret inputs (`image_namespace`, `smtp_host`, `smtp_port`, `smtp_username`,
`smtp_from_address`, `region`, Neon and sizing settings) live in the committed
`terraform/environments/<env>.tfvars`, which `deploy-aws.yml` and a local apply share. Edit and
commit it before the first deploy; `image_namespace` there is also what the Docker Hub existence
check uses. The HCP workspace must hold the same environment as `BIRRAPOINT_ENVIRONMENT`; the
workflow stops when it does not.

## 3. AWS identity for `deploy-aws.yml` (OIDC, no stored key)

A one-time setup in the AWS account, done once per account.

**IAM OIDC provider** for GitHub (once per account; skip if it exists):

```powershell
aws iam list-open-id-connect-providers
aws iam create-open-id-connect-provider `
  --url https://token.actions.githubusercontent.com `
  --client-id-list sts.amazonaws.com
```

AWS validates GitHub's certificate chain itself, so no thumbprint is needed.

**Role `birrapoint-github-deploy`**, trusted only by jobs of this repository that run in the
`aws-production` environment (the `sub` claim names the environment, not a branch; the
environment's main-only branch policy in step 4 is what ties the role to `main`, so it is
mandatory):

```powershell
$repo    = "jesuscorral/birrapoint"
$account = aws sts get-caller-identity --query Account --output text
$provider = "arn:aws:iam::${account}:oidc-provider/token.actions.githubusercontent.com"

$trust = @{
  Version   = "2012-10-17"
  Statement = @(@{
    Effect    = "Allow"
    Principal = @{ Federated = $provider }
    Action    = "sts:AssumeRoleWithWebIdentity"
    Condition = @{ StringEquals = @{
      "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com"
      "token.actions.githubusercontent.com:sub" = "repo:${repo}:environment:aws-production"
    } }
  })
} | ConvertTo-Json -Depth 10
# Written without a BOM (Windows PowerShell 5.1's `Set-Content -Encoding utf8` adds one).
[IO.File]::WriteAllText("$PWD\trust.json", $trust, (New-Object Text.UTF8Encoding $false))

# 2 h sessions: an apply (CloudFront ~15 min) plus the health gate can outlast the 1 h default;
# deploy-aws.yml requests role-duration-seconds: 7200.
$roleArn = aws iam create-role --role-name birrapoint-github-deploy `
  --assume-role-policy-document file://trust.json --max-session-duration 7200 `
  --query Role.Arn --output text
Remove-Item trust.json

aws iam attach-role-policy --role-name birrapoint-github-deploy `
  --policy-arn arn:aws:iam::aws:policy/AdministratorAccess
$roleArn
```

**Permissions.** The role runs the whole apply: VPC, ECS, ALB, CloudFront, IAM roles and policies,
Secrets Manager, CloudWatch Logs and Cloud Map. `AdministratorAccess` is the recommendation **on
an AWS account dedicated to BirraPoint**, where the blast radius is the application itself. It
can be tightened later: take the actions CloudTrail records for a successful apply and teardown,
or start from the service-level managed policies (`AmazonEC2FullAccess`,
`AmazonECS_FullAccess`, `ElasticLoadBalancingFullAccess`, `CloudFrontFullAccess`,
`SecretsManagerReadWrite`, `AWSCloudMapFullAccess`, `CloudWatchLogsFullAccess`) plus a scoped IAM
policy limited to `role/birrapoint-*`. Because of the secret names, run **one environment per AWS
account and region** (`terraform/README.md`).

## 4. GitHub `aws-production` environment

```powershell
# Create the environment, restricted to deployments from main (MANDATORY: see step 3)
@{ deployment_branch_policy = @{ protected_branches = $false; custom_branch_policies = $true } } |
  ConvertTo-Json | gh api --method PUT "repos/$repo/environments/aws-production" --input - | Out-Null
gh api --method POST "repos/$repo/environments/aws-production/deployment-branch-policies" `
  -f name=main -f type=branch | Out-Null

# AWS role, as an environment secret (only jobs in `aws-production` can read it)
gh secret set AWS_ROLE_ARN --env aws-production --body $roleArn

# Terraform inputs from step 2 (values prompted)
gh secret set TF_API_TOKEN  --env aws-production
gh secret set NEON_API_KEY  --env aws-production
gh secret set SMTP_PASSWORD --env aws-production
gh variable set TF_CLOUD_ORGANIZATION --env aws-production --body "<org>"
gh variable set TF_WORKSPACE          --env aws-production --body "<aws workspace>"
gh variable set AWS_REGION            --env aws-production --body "eu-central-1"
# Optional
gh variable set NEON_ORG_ID           --env aws-production --body "org-..."
gh variable set BIRRAPOINT_ENVIRONMENT --env aws-production --body "PROD"
```

`DOCKERHUB_USERNAME` is a repository secret already set for `ci.yml`; `DOCKERHUB_READ_TOKEN`
(optional) is read from the repository too.

Then add **required reviewers** in Settings, Environments, `aws-production`, Required reviewers.
With reviewers set, every deploy waits for an approval after its inputs (and the existence of the
release images) have been validated. Check that *Deployment branches and tags* shows only `main`.
`deploy-aws.yml` also refuses to run from any other branch.

## 5. The release bot and `main`

Shared by both clouds: see [`../ci-setup.md` §2](../ci-setup.md#2-the-release-bot-and-main).

## Everyday use

```powershell
# Release version.txt's X.Y.Z from main (or a hotfix branch), advancing version.txt by a patch
gh workflow run release.yml -f ref=main -f bump=patch

# Deploy it to AWS production (then approve in the Actions tab)
gh workflow run deploy-aws.yml -f version=0.1.0

# Mixed versions / rollback: override single components (rollback = apply the previous version)
gh workflow run deploy-aws.yml -f version=0.1.1 -f keycloak_version=0.1.0
```

The job ends with a health gate (`aws ecs wait services-stable`, up to three 10-minute attempts)
and a summary with the PWA and Keycloak URLs and the deployed versions. If the gate fails it
prints the services' latest events; the same details are in the ECS console and in the
`/birrapoint/<env>/<service>` log groups.
