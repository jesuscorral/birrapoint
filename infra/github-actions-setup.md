# GitHub Actions setup (FR-064)

This page covers the one-time configuration behind the CI/CD pipelines. Nothing in it is stored
in the repository.

| Workflow | Trigger | Does | Needs |
|---|---|---|---|
| `ci.yml` | PRs and pushes to `main` | Quality gates; on `main`, pushes `<ns>/birrapoint-*:latest` + `:sha-<short>` | Docker Hub |
| `infra.yml` | Changes to `infra/**` | `terraform fmt`/`validate` (no backend), Pester | nothing |
| `workflows.yml` | Changes to `.github/**` | actionlint, release script tests | nothing |
| `release.yml` | Manual | Publishes `<ns>/birrapoint-*-release:X.Y.Z`, tag, GitHub Release, bumps `version.txt` | Docker Hub |
| `deploy.yml` | Manual, approval | Rolls production to a release (`infra/deploy.ps1 -AppsOnly`) | Docker Hub, Azure OIDC, `production` environment |

No workflow creates or changes infrastructure. That is a deliberate, local, full
`infra/deploy.ps1` run (see `infra/terraform/README.md`). `deploy.yml` assumes the environment
already exists.

Commands below are PowerShell. Run them from the repository root, logged in with `az login` and
`gh auth login`.

## 1. Docker Hub (CI, release and deploy)

1. Create a Docker Hub **personal access token** with **Read & Write** scope: Docker Hub →
   Account settings → Personal access tokens.
2. Store it:

```powershell
gh secret set DOCKERHUB_USERNAME --body "<docker hub user>"
gh secret set DOCKERHUB_TOKEN            # paste the token when prompted
gh variable set IMAGE_NAMESPACE --body "<docker hub user or org>"
```

`IMAGE_NAMESPACE` must be a **variable**, not a secret. The workflows read `vars.IMAGE_NAMESPACE`,
and a secret would also mask every image name in the logs as `***`. The six repositories
(`birrapoint-{api,web,keycloak}` and `birrapoint-{api,web,keycloak}-release`) are created by the
first push.

## 2. Azure identity for `deploy.yml` (OIDC, no stored password)

The deploy identity only needs to update the Container Apps in the application resource group
(`rg-<name_prefix>`, `rg-birrapoint` by default). It never touches Terraform state or Neon.

```powershell
$repo = "jesuscorral/birrapoint"
$rg   = "rg-birrapoint"
$sub  = az account show --query id --output tsv
$tenant = az account show --query tenantId --output tsv

# App registration + service principal
$appId = az ad app create --display-name "birrapoint-github-deploy" --query appId --output tsv
az ad sp create --id $appId | Out-Null

# Trust GitHub's OIDC token, but only for jobs running in the `production` environment
@{
  name      = "github-production"
  issuer    = "https://token.actions.githubusercontent.com"
  subject   = "repo:${repo}:environment:production"
  audiences = @("api://AzureADTokenExchange")
} | ConvertTo-Json | Set-Content -Encoding utf8 federated.json
az ad app federated-credential create --id $appId --parameters "@federated.json"
Remove-Item federated.json

# Rights on the application resource group only (Contributor includes the Container Apps
# `listSecrets` action that `az containerapp update` needs)
az role assignment create --assignee $appId --role Contributor `
  --scope "/subscriptions/$sub/resourceGroups/$rg"
```

To narrow the permissions further, replace Contributor with a custom role limited to
`Microsoft.App/containerApps/*` and `Microsoft.App/containerApps/revisions/*` on that resource
group.

## 3. GitHub `production` environment

```powershell
# Create the environment (no-op if it exists)
gh api --method PUT "repos/$repo/environments/production" | Out-Null

# Azure identity, as environment secrets (only jobs in `production` can read them)
gh secret set AZURE_CLIENT_ID       --env production --body $appId
gh secret set AZURE_TENANT_ID       --env production --body $tenant
gh secret set AZURE_SUBSCRIPTION_ID --env production --body $sub
```

Then add **required reviewers** in Settings → Environments → `production` → Required
reviewers. With reviewers set, every deploy waits for an approval after its inputs have been
validated. Optionally, also restrict the environment's deployment branches to `main`.

If you changed Terraform's `name_prefix`, set it for `deploy.yml` too:
`gh variable set NAME_PREFIX --body "<prefix>"`.

## 4. The release bot and `main`

`release.yml` pushes the `vX.Y.Z` tag and the `version.txt` bump with the workflow's own
`GITHUB_TOKEN` (`contents: write`). This works as long as `main` is **not protected**, which is
the case today.

If you protect `main` with a ruleset that requires pull requests, `GITHUB_TOKEN` can no longer
push the bump. In that case:

1. Create a GitHub App with *Contents: Read & write*, install it on the repository, and add it to
   the ruleset's **bypass list**.
2. In `release.yml`'s `finalize` job, mint a token with `actions/create-github-app-token`
   (pinned by SHA), and use it for the checkout and pushes instead of `GITHUB_TOKEN`.

If CI later becomes a required check, remember that docs-only PRs never produce it, because of
the path filters (T137).

## Everyday use

```powershell
# Release version.txt's X.Y.Z from main (or a hotfix branch), advancing version.txt by a patch
gh workflow run release.yml -f ref=main -f bump=patch

# Deploy it to production (then approve in the Actions tab)
gh workflow run deploy.yml -f version=0.1.0

# Mixed versions / rollback: override single components
gh workflow run deploy.yml -f version=0.1.1 -f keycloak_version=0.1.0
```
