# GitHub Actions setup: Azure (FR-064)

This page covers the one-time configuration behind the CI/CD pipeline of the **Azure** deployment
(`deploy-azure.yml`, GitHub environment `azure-production`; each cloud has its own independent
deploy target, the AWS one is in [`../aws/github-actions-setup.md`](../aws/github-actions-setup.md)).
Nothing in it is stored in the repository. The workflow overview and everything shared by both
clouds (Docker Hub, `IMAGE_NAMESPACE`, the release bot and `main` protection) is in
[`../ci-setup.md`](../ci-setup.md).

Only `deploy-azure.yml` creates or changes Azure infrastructure: it runs the same `terraform apply`
as a workstation (see `terraform/README.md`), so it can also create a brand-new environment.
State lives in HCP Terraform (workspace in **Local** execution mode).

For the full from-scratch order (accounts, keys, first local apply) see [`deployment-runbook.md`](deployment-runbook.md).

Commands below are PowerShell. Run them from the repository root, logged in with `az login` and
`gh auth login`.

## 1. Docker Hub (CI, release and deploy)

Shared by both clouds: follow [`../ci-setup.md` §1](../ci-setup.md#1-docker-hub-ci-release-and-deploys).
`DOCKERHUB_READ_TOKEN` (optional, private repositories) is read by `deploy-azure.yml`.

## 2. HCP Terraform, Neon and SMTP (state and apply inputs)

1. In HCP Terraform create an organization and a workspace, set its execution mode to **Local**
   (HCP stores the state only), and create a team/user API token.
2. Store the settings. Values marked *secret* go to the `azure-production` environment, see step 4.

| Kind | Name | Value |
|---|---|---|
| secret | `TF_API_TOKEN` | HCP Terraform API token |
| secret | `NEON_API_KEY` | Neon API key |
| secret | `SMTP_PASSWORD` | SMTP relay password |
| variable | `TF_CLOUD_ORGANIZATION`, `TF_WORKSPACE` | HCP organization and workspace |
| variable (optional) | `NEON_ORG_ID` | Neon organization id (not committed); needed when the account uses organizations (Neon answers `org_id is required`) |
| variable (optional) | `BIRRAPOINT_ENVIRONMENT` | environment name, default `PROD`; selects `terraform/environments/<env>.tfvars` |

Non-secret inputs (`image_namespace`, `smtp_host`, `smtp_port`, `smtp_username`,
`smtp_from_address`, Neon and sizing settings) are **not** GitHub variables any more: they live in
the committed `terraform/environments/<env>.tfvars`, which `deploy-azure.yml` and a local apply
share. Edit that file (and commit it) before the first deploy; `image_namespace` there is also what
the Docker Hub existence check in `deploy-azure.yml` uses. The HCP workspace must hold the same
environment as `BIRRAPOINT_ENVIRONMENT`; the workflow stops when it does not.

## 3. Azure identity for `deploy-azure.yml` (OIDC, no stored password)

The deploy identity runs the whole Terraform apply: it creates the resource group, Container Apps,
Key Vault and role assignments. It therefore needs **Contributor and User Access Administrator**
(or RBAC Administrator) on the subscription; a resource-group scope is not enough for a brand-new
environment, because the group does not exist yet.

```powershell
$repo = "jesuscorral/birrapoint"
$sub  = az account show --query id --output tsv
$tenant = az account show --query tenantId --output tsv

# App registration + service principal
$appId = az ad app create --display-name "birrapoint-github-deploy" --query appId --output tsv
az ad sp create --id $appId | Out-Null

# Trust GitHub's OIDC token, but only for jobs running in the `azure-production` environment.
# Written without a BOM (Windows PowerShell 5.1's `Set-Content -Encoding utf8` adds one).
$federated = @{
  name      = "github-azure-production"
  issuer    = "https://token.actions.githubusercontent.com"
  subject   = "repo:${repo}:environment:azure-production"
  audiences = @("api://AzureADTokenExchange")
} | ConvertTo-Json
[IO.File]::WriteAllText("$PWD\federated.json", $federated, (New-Object Text.UTF8Encoding $false))
az ad app federated-credential create --id $appId --parameters "@federated.json"
Remove-Item federated.json

# Using the object id avoids a Graph lookup that can fail while the new service principal is
# still replicating.
$spObjectId = az ad sp show --id $appId --query id --output tsv
foreach ($role in "Contributor", "User Access Administrator") {
  az role assignment create --assignee-object-id $spObjectId --assignee-principal-type ServicePrincipal `
    --role $role --scope "/subscriptions/$sub"
}
```

Also add this identity to `key_vault_secrets_officer_principal_ids` if an operator group is used
(`terraform/README.md`). The OIDC subject names the environment, not a branch. The environment's main-only branch
policy in step 4 is what ties this identity to `main`. It is mandatory, and more so now that the
identity is broad.

## 4. GitHub `azure-production` environment

```powershell
# Create the environment, restricted to deployments from main (MANDATORY: see step 3)
@{ deployment_branch_policy = @{ protected_branches = $false; custom_branch_policies = $true } } |
  ConvertTo-Json | gh api --method PUT "repos/$repo/environments/azure-production" --input - | Out-Null
gh api --method POST "repos/$repo/environments/azure-production/deployment-branch-policies" `
  -f name=main -f type=branch | Out-Null

# Azure identity, as environment secrets (only jobs in `azure-production` can read them)
gh secret set AZURE_CLIENT_ID       --env azure-production --body $appId
gh secret set AZURE_TENANT_ID       --env azure-production --body $tenant
gh secret set AZURE_SUBSCRIPTION_ID --env azure-production --body $sub

# Terraform inputs from step 2 (values prompted)
gh secret set TF_API_TOKEN  --env azure-production
gh secret set NEON_API_KEY  --env azure-production
gh secret set SMTP_PASSWORD --env azure-production
gh variable set TF_CLOUD_ORGANIZATION --body "<org>"
gh variable set TF_WORKSPACE          --body "<workspace>"
```

Then add **required reviewers** in Settings → Environments → `azure-production` → Required
reviewers. With reviewers set, every deploy waits for an approval after its inputs (and the
existence of the release images) have been validated. Check that *Deployment branches and tags*
shows only `main`. `deploy-azure.yml` also refuses to run from any other branch.

If you deploy an environment other than `PROD`, set it for `deploy-azure.yml` too:
`gh variable set BIRRAPOINT_ENVIRONMENT --body "<environment>"`.

## 5. The release bot and `main`

Shared by both clouds: see [`../ci-setup.md` §2](../ci-setup.md#2-the-release-bot-and-main).
`deploy-azure.yml` refuses to run unless dispatched from `main`.

## Everyday use

```powershell
# Release version.txt's X.Y.Z from main (or a hotfix branch), advancing version.txt by a patch
gh workflow run release.yml -f ref=main -f bump=patch

# Deploy it to production (then approve in the Actions tab)
gh workflow run deploy-azure.yml -f version=0.1.0

# Mixed versions / rollback: override single components (rollback = apply the previous version)
gh workflow run deploy-azure.yml -f version=0.1.1 -f keycloak_version=0.1.0
```
