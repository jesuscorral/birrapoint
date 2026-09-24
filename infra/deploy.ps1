<#
.SYNOPSIS
    Single-command BirraPoint cloud deployment (FR-045 / SC-011): Terraform remote-state
    bootstrap, Docker Hub image build + push, `terraform apply`.

.DESCRIPTION
    Idempotent; safe to re-run for every release. Steps:
      1. Verifies prerequisites (az, terraform, docker, git; `az login`; NEON_API_KEY).
      2. Creates the Terraform state resource group / storage account / container if missing.
      3. Builds birrapoint-api, birrapoint-web and birrapoint-keycloak and pushes them to
         Docker Hub as <ImageNamespace>/<name>:<ImageTag> (skip with -SkipBuild).
      4. Runs `terraform init` against the remote state and `terraform apply`.

    Prerequisites (documented in infra/terraform/README.md): Azure CLI logged in (`az login`),
    Docker running and logged in to Docker Hub (`docker login`), Terraform >= 1.9, the
    NEON_API_KEY environment variable, and infra/terraform/terraform.tfvars (copy
    terraform.tfvars.example). Compatible with Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
    ./infra/deploy.ps1 -ImageNamespace myhubuser
.EXAMPLE
    ./infra/deploy.ps1 -ImageNamespace myhubuser -SkipBuild -ImageTag 4befbbf -AutoApprove
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ImageNamespace,

    # Defaults to the current commit's short SHA; the working tree must then be clean so the tag
    # really identifies the deployed source.
    [string] $ImageTag,

    [string] $VarFile = (Join-Path $PSScriptRoot 'terraform/terraform.tfvars'),

    [string] $Location = 'westeurope',
    [string] $StateResourceGroup = 'rg-birrapoint-tfstate',
    # Globally unique; derived from the subscription id when omitted.
    [string] $StateStorageAccount,
    [string] $StateContainer = 'tfstate',
    [string] $StateKey = 'birrapoint.tfstate',

    [switch] $SkipBuild,
    [switch] $AllowDirty,
    [switch] $AutoApprove
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$terraformDir = Join-Path $PSScriptRoot 'terraform'

function Invoke-Native {
    # Runs a native command and throws on a non-zero exit code (PowerShell 5.1 does not).
    param([string] $Description, [scriptblock] $Command)
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Description failed (exit code $LASTEXITCODE)." }
}

function Assert-Command([string] $Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Prerequisite '$Name' is not on PATH (see infra/terraform/README.md)."
    }
}

# --- 1. Prerequisites ------------------------------------------------------------------------

foreach ($tool in 'az', 'terraform', 'git') { Assert-Command $tool }
if (-not $SkipBuild) { Assert-Command 'docker' }

if (-not $env:NEON_API_KEY) { throw 'NEON_API_KEY is not set (Neon console -> Account settings -> API keys).' }
if (-not (Test-Path $VarFile)) {
    throw "Variables file '$VarFile' not found. Copy infra/terraform/terraform.tfvars.example and fill it in."
}

# Windows PowerShell 5.1 turns a native command's redirected stderr into terminating errors
# under ErrorActionPreference=Stop, so probes whose failure is an expected answer run with
# 'Continue' locally.
function Invoke-Probe([scriptblock] $Command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Command 2>$null } finally { $ErrorActionPreference = $previous }
}

$accountJson = (Invoke-Probe { az account show --output json }) -join "`n"
$account = if ($accountJson) { $accountJson | ConvertFrom-Json } else { $null }
if (-not $account) { throw 'Not logged in to Azure. Run `az login` first.' }
$subscriptionId = $account.id
Write-Host "Azure subscription: $($account.name) ($subscriptionId)"

if (-not $ImageTag) {
    $dirty = git -C $repoRoot status --porcelain
    if ($dirty -and -not $AllowDirty) {
        throw 'Working tree has uncommitted changes; commit them, pass -ImageTag, or use -AllowDirty.'
    }
    $ImageTag = (git -C $repoRoot rev-parse --short HEAD).Trim()
    if ($dirty) { $ImageTag = "$ImageTag-dirty" }
}
Write-Host "Image tag: $ImageTag"

if (-not $StateStorageAccount) {
    # 3-24 lowercase alphanumerics, globally unique: stable per subscription.
    $StateStorageAccount = ('stbirrapointtf' + ($subscriptionId -replace '-', '').Substring(0, 10)).ToLower()
}

# --- 2. Terraform remote state (idempotent) --------------------------------------------------

Invoke-Native "Ensure state resource group '$StateResourceGroup'" {
    az group create --name $StateResourceGroup --location $Location --output none
}

# `list` + filter instead of `show`: a missing account is an empty result, not an error.
$existing = az storage account list --resource-group $StateResourceGroup `
    --query "[?name=='$StateStorageAccount'].name" --output tsv
if ($LASTEXITCODE -ne 0) { throw 'Listing state storage accounts failed.' }
if (-not $existing) {
    Invoke-Native "Create state storage account '$StateStorageAccount'" {
        az storage account create --name $StateStorageAccount --resource-group $StateResourceGroup `
            --location $Location --sku Standard_LRS --kind StorageV2 --min-tls-version TLS1_2 `
            --allow-blob-public-access false --https-only true --output none
    }
}

$storageAccountKey = az storage account keys list --account-name $StateStorageAccount --resource-group $StateResourceGroup --query '[0].value' --output tsv
if (-not $storageAccountKey) { throw 'Could not read the state storage account key.' }
Invoke-Native "Ensure state container '$StateContainer'" {
    az storage container create --name $StateContainer --account-name $StateStorageAccount `
        --account-key $storageAccountKey --auth-mode key --output none
}

# --- 3. Images -> Docker Hub ------------------------------------------------------------------

if (-not $SkipBuild) {
    $images = @(
        @{ Name = 'birrapoint-api'; Context = 'backend'; Dockerfile = 'backend/src/BirraPoint.Api/Dockerfile' },
        @{ Name = 'birrapoint-web'; Context = 'frontend'; Dockerfile = 'frontend/Dockerfile' },
        @{ Name = 'birrapoint-keycloak'; Context = 'infra/keycloak'; Dockerfile = 'infra/keycloak/Dockerfile' }
    )
    foreach ($image in $images) {
        $ref = "$ImageNamespace/$($image.Name):$ImageTag"
        Invoke-Native "Build $ref" {
            docker build --file (Join-Path $repoRoot $image.Dockerfile) --tag $ref (Join-Path $repoRoot $image.Context)
        }
        Invoke-Native "Push $ref" { docker push $ref }
    }
}

# --- 4. Terraform ----------------------------------------------------------------------------

Invoke-Native 'terraform init' {
    terraform -chdir="$terraformDir" init -input=false -reconfigure `
        "-backend-config=resource_group_name=$StateResourceGroup" `
        "-backend-config=storage_account_name=$StateStorageAccount" `
        "-backend-config=container_name=$StateContainer" `
        "-backend-config=key=$StateKey"
}

$applyArgs = @(
    "-chdir=$terraformDir", 'apply', '-input=false',
    "-var-file=$((Resolve-Path $VarFile).Path)",
    "-var=subscription_id=$subscriptionId",
    "-var=location=$Location",
    "-var=image_namespace=$ImageNamespace",
    "-var=image_tag=$ImageTag"
)
if ($AutoApprove) { $applyArgs += '-auto-approve' }
Invoke-Native 'terraform apply' { terraform @applyArgs }

Write-Host ''
Write-Host 'Deployed.' -ForegroundColor Green
terraform -chdir="$terraformDir" output
