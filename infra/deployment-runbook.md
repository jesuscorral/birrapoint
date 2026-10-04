# Azure deployment runbook: from zero to a running environment

This is the end-to-end checklist for deploying BirraPoint to **Azure** from scratch (ADR-0022). It lists every
account, key and setting needed, in the order to create them. The details behind each step are in
[`azure/terraform/README.md`](azure/terraform/README.md) (local apply) and
[`github-actions-setup.md`](github-actions-setup.md) (CI).

There are two phases:

- **Phase A**: the first deploy, from your workstation with your own `az login`.
- **Phase B**: every later deploy, through `deploy-azure.yml` in GitHub Actions.

Both phases run the same `terraform apply` against the same HCP Terraform state, with the same
committed `infra/azure/terraform/environments/prod.tfvars`.

## 1. Accounts and credentials

| # | Service | What to create | Used by | Where it goes |
|---|---|---|---|---|
| 1 | **Azure** | A subscription where you are **Owner** (or Contributor + User Access Administrator) | Terraform (resources + role assignments) | `az login`; `ARM_SUBSCRIPTION_ID` |
| 2 | **HCP Terraform** (free) | Organization + one workspace per environment, execution mode **Local** | Terraform state | `TF_CLOUD_ORGANIZATION`, `TF_WORKSPACE` |
| 3 | HCP Terraform | **User token** (via `terraform login`) for your workstation | Local apply / teardown | `%APPDATA%\terraform.d\credentials.tfrc.json` (written by `terraform login`) |
| 4 | HCP Terraform | **Team token** for a team that can only access this workspace | `deploy-azure.yml` | GitHub secret `TF_API_TOKEN` |
| 5 | **Neon** | Account + **API key** | Terraform (Neon project, roles, databases); teardown | `NEON_API_KEY` env var; GitHub secret `NEON_API_KEY` |
| 6 | Neon | Organization id (`org-...`), only if the key spans several organizations | Terraform; teardown `-NeonOrgId` | `TF_VAR_neon_org_id`; GitHub variable `NEON_ORG_ID` |
| 7 | **Docker Hub** | Account (user or organization = image namespace) | Image hosting | `image_namespace` in `prod.tfvars`; GitHub variable `IMAGE_NAMESPACE` |
| 8 | Docker Hub | **Personal access token, Read & Write** | `ci.yml`, `release.yml` push images | GitHub secrets `DOCKERHUB_USERNAME`, `DOCKERHUB_TOKEN` |
| 9 | Docker Hub | *Optional:* **Read-only** access token (private repos, or to avoid pull rate limits) | Container Apps pull; `deploy-azure.yml` image check | `dockerhub_token` in `terraform.tfvars`; GitHub secret `DOCKERHUB_READ_TOKEN` |
| 10 | **SMTP provider** | SMTP credentials + a **verified sender address** | API emails (judge invitations, results) | `smtp_*` in `prod.tfvars`; password in `terraform.tfvars` / GitHub secret `SMTP_PASSWORD` |
| 11 | **Microsoft Entra ID** | App registration `birrapoint-github-deploy` with a federated credential (OIDC, no password) | `deploy-azure.yml` logs in to Azure | GitHub secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` |
| 12 | Microsoft Entra ID | *Recommended:* security group `birrapoint-terraform-operators` holding you and the deploy identity | Key Vault Secrets Officer for everyone who runs Terraform | `key_vault_secrets_officer_principal_ids` in `prod.tfvars` |
| 13 | **GitHub** | Repository admin; `azure-production` environment with required reviewers | `deploy-azure.yml` approval gate | Repository settings |

Terraform generates every other secret itself: the Keycloak admin password, the API admin-client
secret and the Neon role passwords. It stores them in Key Vault and in the HCP state.

> **Security:** the HCP state holds every generated production secret in plain text. Anyone with
> access to the workspace or a token for it can read them, so use a team token scoped to this
> workspace only for CI. Never commit any of the secrets above. `terraform.tfvars` is gitignored.

## 2. Workstation tools

| Tool | Version | Needed for |
|---|---|---|
| Azure CLI (`az`) | current | Login, verification, teardown sweep |
| Terraform | 1.14.3 (same as `deploy-azure.yml`; `>= 1.9` works) | Apply / destroy |
| GitHub CLI (`gh`) | current | Secrets, variables, workflow runs |
| PowerShell 7 + Pester 5 | — | `teardown.ps1` and its tests |

No local Docker is needed: images are built and published by GitHub Actions.

## Phase A: first deploy from the workstation

### A1. Publish the images

`deploy-azure.yml` and Terraform only pull images; they never build them.

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

1. Sign up at <https://app.terraform.io>, create an organization (e.g. `birrapoint`).
2. Create a workspace (e.g. `birrapoint-prod`) of type **CLI-driven workflow**.
3. Workspace → Settings → General → **Execution Mode: Local**, then save. If it stays on Remote,
   the apply runs on HCP's servers, which have no Azure credentials, and fails.

### A3. Neon and SMTP

1. Neon console → Account settings → **API keys** → create a key.
2. If you belong to several Neon organizations, copy the organization id (`org-...`) from the
   organization settings.
3. From your SMTP provider, collect the host, port (587 with STARTTLS), user name, password, and a
   verified sender address.

### A4. Fill the environment file (committed)

Edit `infra/azure/terraform/environments/prod.tfvars` and commit it through a PR:

```hcl
image_namespace   = "<docker hub user or org>"   # CHANGE-ME is rejected by a validation
smtp_host         = "<smtp host>"
smtp_port         = 587
smtp_username     = "<smtp user>"
smtp_from_address = "<verified sender>"
key_vault_secrets_officer_principal_ids = ["<object id of the Entra group from item 12>"]
```

The Entra group (item 12) matters as soon as two identities run Terraform: you locally and the
CI identity. Each apply grants Secrets Officer only to the identity running it plus this list, so
without the group the CI apply drops your access, and your next local plan or teardown fails with
`403 ForbiddenByRbac`. You can create the group and add yourself now, and add the CI identity in
step B1:

```powershell
$groupId = az ad group create --display-name birrapoint-terraform-operators `
  --mail-nickname birrapoint-terraform-operators --query id --output tsv
az ad group member add --group $groupId --member-id (az ad signed-in-user show --query id --output tsv)
$groupId   # goes into key_vault_secrets_officer_principal_ids
```

### A5. Local secrets (not committed)

```powershell
Copy-Item infra/azure/terraform/terraform.tfvars.example infra/azure/terraform/terraform.tfvars
# edit: smtp_password = "...", optionally dockerhub_username / dockerhub_token (read-only)
```

### A6. Apply

```powershell
az login
az account set --subscription "<subscription id>"
terraform login                                   # stores the HCP user token

$env:TF_CLOUD_ORGANIZATION = "<org>"
$env:TF_WORKSPACE          = "<workspace>"
$env:ARM_SUBSCRIPTION_ID   = "<subscription id>"
$env:NEON_API_KEY          = "<neon api key>"
# $env:TF_VAR_neon_org_id  = "org-..."            # only with several Neon organizations

terraform -chdir=infra/azure/terraform init
terraform -chdir=infra/azure/terraform plan  -var-file=environments/prod.tfvars -var release_version=X.Y.Z
terraform -chdir=infra/azure/terraform apply -var-file=environments/prod.tfvars -var release_version=X.Y.Z
```

A first apply takes several minutes longer than later ones, because it waits for Key Vault role
propagation.

### A7. Verify

```powershell
terraform -chdir=infra/azure/terraform output web_url
terraform -chdir=infra/azure/terraform output keycloak_url
terraform -chdir=infra/azure/terraform output -raw keycloak_admin_password   # Keycloak admin, user "admin"
az containerapp revision list -g birrapoint-prod-rg -n birrapoint-prod-api -o table   # repeat for -web, -kc
```

1. Open `web_url`, self-register an organizer from the login page (no seeded account exists in
   production), and send one judge invitation to check SMTP.
2. Log in to `<keycloak_url>/admin` and change the `admin` password (T130 hardening is pending).

## Phase B: later deploys through GitHub Actions

### B1. Azure deploy identity (OIDC)

Follow [`github-actions-setup.md` §3](github-actions-setup.md#3-azure-identity-for-deploy-azureyml-oidc-no-stored-password).
It creates the app registration, a federated credential limited to the `azure-production` environment,
and **Contributor + User Access Administrator** on the subscription.

Then add the identity to the operators group from A4:

```powershell
az ad group member add --group <group id> --member-id $spObjectId
```

### B2. HCP team token

HCP Terraform → organization Settings → Teams: create a team (e.g. `github-deploy`) with access to
the workspace only, then create a **team token**. Team management may need a paid HCP plan. On
the free plan, fall back to an organization or user token and keep the security note in §1 in
mind.

### B3. GitHub `azure-production` environment, secrets and variables

Follow [`github-actions-setup.md` §4](github-actions-setup.md#4-github-azure-production-environment).
Here is everything `deploy-azure.yml` reads:

| Kind | Name | Value |
|---|---|---|
| env secret | `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` | From B1 |
| env secret | `TF_API_TOKEN` | HCP team token (B2) |
| env secret | `NEON_API_KEY` | Neon API key |
| env secret | `SMTP_PASSWORD` | SMTP password |
| secret | `DOCKERHUB_USERNAME` | Docker Hub user (already set in A1) |
| secret (optional) | `DOCKERHUB_READ_TOKEN` | Read-only Docker Hub token |
| variable | `TF_CLOUD_ORGANIZATION`, `TF_WORKSPACE` | Same as A6 |
| variable (optional) | `NEON_ORG_ID` | Neon organization id |
| variable (optional) | `BIRRAPOINT_ENVIRONMENT` | Default `PROD` |

Add **required reviewers** to the `azure-production` environment, and restrict it to `main`.

### B4. Deploy

```powershell
gh workflow run release.yml -f ref=main -f bump=patch      # new X.Y.Z
gh workflow run deploy-azure.yml  -f version=X.Y.Z               # approve in the Actions tab
gh workflow run deploy-azure.yml  -f version=X.Y.Z -f keycloak_version=X.Y.W   # per-component override / rollback
```

The workflow validates the version and checks that the images exist on Docker Hub. It waits for
approval, then guards that the HCP workspace holds the same environment. It then applies and
waits for every Container App revision to become healthy.

## Tear down (wipes everything, data included)

```powershell
$env:NEON_API_KEY = "<key>"   # plus TF_CLOUD_ORGANIZATION, TF_WORKSPACE, terraform login, az login
./infra/azure/teardown.ps1 -WhatIf  # preview
./infra/azure/teardown.ps1          # type the resource group name to confirm
```

Accounts, tokens, Docker Hub images, GitHub secrets and the HCP workspace survive a teardown. The
next deploy is just step A6 (or B4) again.

## Checklist

- [ ] Azure subscription, Owner role
- [ ] HCP organization + workspace in **Local** mode; `terraform login` done
- [ ] Neon API key (+ org id if needed)
- [ ] Docker Hub account + Read & Write token in GitHub; `IMAGE_NAMESPACE` variable
- [ ] `latest` images (merge to `main`) and a release `X.Y.Z` (`release.yml`)
- [ ] SMTP credentials + verified sender
- [ ] `prod.tfvars` filled and merged (`image_namespace`, `smtp_*`, operators group)
- [ ] `terraform.tfvars` with `smtp_password`
- [ ] First local `init` + `apply`; app reachable, invitation email received
- [ ] Keycloak `admin` password changed
- [ ] Entra app registration + federated credential + roles; added to operators group
- [ ] HCP team token
- [ ] GitHub `azure-production` environment: secrets, variables, reviewers, `main`-only
- [ ] `deploy-azure.yml` run end to end
