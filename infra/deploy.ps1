<#
.SYNOPSIS
    Single-command BirraPoint cloud deployment (FR-045 / SC-011, FR-064): Terraform remote-state
    bootstrap, `terraform apply`, then a rollout of the chosen Docker Hub image to each Container App.

.DESCRIPTION
    Idempotent; safe to re-run. Images are never built here: ci.yml publishes
    <ns>/birrapoint-<component>:latest and release.yml publishes the immutable
    <ns>/birrapoint-<component>-release:X.Y.Z. Per component (api, web, keycloak):
      - -ApiVersion / -WebVersion / -KeycloakVersion X.Y.Z  -> <ns>/birrapoint-<component>-release:X.Y.Z
      - version omitted                                    -> <ns>/birrapoint-<component>:latest

    Steps:
      1. Verifies prerequisites and that every selected image exists on Docker Hub.
      2. (skipped with -AppsOnly) Creates the Terraform state resource group / storage account /
         container if missing, then runs `terraform init` + `terraform apply`. Terraform uses the
         images only when it first creates a Container App (lifecycle ignore_changes, T134).
      3. Rolls each app to its image with `az containerapp update` — keycloak, then api (which
         migrates the database on startup), then web — waiting for each new revision to become
         healthy before moving on. A pinned version the app already runs is skipped.

    -AppsOnly is the image-only deployment used by the deploy pipeline (deploy.yml): no Terraform,
    tfvars, NEON_API_KEY or state access — only `az login` with rights on the application
    resource group. Compatible with Windows PowerShell 5.1 and PowerShell 7. Supports -WhatIf.

    Prerequisites (documented in infra/terraform/README.md): Azure CLI logged in (`az login`);
    without -AppsOnly also Terraform >= 1.9, the NEON_API_KEY environment variable and
    infra/terraform/terraform.tfvars (copy terraform.tfvars.example).

.EXAMPLE
    ./infra/deploy.ps1 -ImageNamespace myhubuser
    Full deployment of the latest images.
.EXAMPLE
    ./infra/deploy.ps1 -ImageNamespace myhubuser -AppsOnly -ApiVersion 0.3.1 -WebVersion 0.3.1 -KeycloakVersion 0.3.1
    Roll the apps to release 0.3.1 without touching infrastructure.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string] $ImageNamespace,

    # Release versions (X.Y.Z); an omitted version deploys that component's `latest` image.
    [string] $ApiVersion,
    [string] $WebVersion,
    [string] $KeycloakVersion,

    # Image rollout only: skip state bootstrap and Terraform.
    [switch] $AppsOnly,

    # -AppsOnly only: must match Terraform's name_prefix (Container App and resource group names).
    [string] $NamePrefix = 'birrapoint',

    # Defaults to infra/terraform/terraform.tfvars.
    [string] $VarFile,

    [string] $Location = 'westeurope',
    [string] $StateResourceGroup = 'rg-birrapoint-tfstate',
    # Globally unique; derived from the subscription id when omitted.
    [string] $StateStorageAccount,
    [string] $StateContainer = 'tfstate',
    [string] $StateKey = 'birrapoint.tfstate',

    # Per app: how long to wait for its new revision to become healthy.
    [int] $RevisionTimeoutSeconds = 600,

    [switch] $AutoApprove
)

$ErrorActionPreference = 'Stop'
$terraformDir = Join-Path $PSScriptRoot 'terraform'
# Resolved here, not as a param default: Windows PowerShell 5.1 leaves $PSScriptRoot empty in
# param defaults when the script is run with `powershell -File`.
if (-not $VarFile) { $VarFile = Join-Path $terraformDir 'terraform.tfvars' }
Import-Module (Join-Path $PSScriptRoot 'DeployImages.psm1') -Force

# `az containerapp` subcommands outside the core CLI install their extension without prompting
# (non-interactive runs in CI).
$env:AZURE_EXTENSION_USE_DYNAMIC_INSTALL = 'yes_without_prompt'

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

# Windows PowerShell 5.1 turns a native command's redirected stderr into terminating errors
# under ErrorActionPreference=Stop, so probes whose failure is an expected answer run with
# 'Continue' locally.
function Invoke-Probe([scriptblock] $Command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Command 2>$null } finally { $ErrorActionPreference = $previous }
}

function Invoke-AzJson {
    # Runs an az query and returns the parsed JSON; throws on failure.
    param([string] $Description, [scriptblock] $Command)
    $json = (& $Command) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "$Description failed (exit code $LASTEXITCODE)." }
    if ($json) { $json | ConvertFrom-Json } else { $null }
}

function Wait-HealthyRevision {
    param([string] $App, [string] $ResourceGroup, [int] $TimeoutSeconds)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($true) {
        $state = Invoke-AzJson "Read $App state" {
            az containerapp show --name $App --resource-group $ResourceGroup `
                --query '{latest: properties.latestRevisionName, ready: properties.latestReadyRevisionName}' --output json
        }
        $revision = Invoke-AzJson "Read revision $($state.latest)" {
            az containerapp revision show --name $App --resource-group $ResourceGroup --revision $state.latest `
                --query '{health: properties.healthState, provisioning: properties.provisioningState, running: properties.runningState}' --output json
        }
        Write-Host ("    {0}: provisioning={1} running={2} health={3}" -f $state.latest, $revision.provisioning, $revision.running, $revision.health)

        if ($revision.provisioning -eq 'Failed' -or $revision.running -eq 'Failed') {
            throw "Revision $($state.latest) of $App failed (provisioning=$($revision.provisioning), running=$($revision.running)). Check its logs; the previous revision keeps serving until a healthy one replaces it."
        }
        if ($revision.health -eq 'Healthy' -and $state.ready -eq $state.latest) { return }
        if ((Get-Date) -gt $deadline) {
            throw "Revision $($state.latest) of $App did not become healthy within $TimeoutSeconds s."
        }
        Start-Sleep -Seconds 10
    }
}

# --- 1. Prerequisites and images -------------------------------------------------------------

Assert-Command 'az'
if (-not $AppsOnly) {
    Assert-Command 'terraform'
    if (-not $env:NEON_API_KEY) { throw 'NEON_API_KEY is not set (Neon console -> Account settings -> API keys).' }
    if (-not (Test-Path $VarFile)) {
        throw "Variables file '$VarFile' not found. Copy infra/terraform/terraform.tfvars.example and fill it in."
    }
}

$accountJson = (Invoke-Probe { az account show --output json }) -join "`n"
$account = if ($accountJson) { $accountJson | ConvertFrom-Json } else { $null }
if (-not $account) { throw 'Not logged in to Azure. Run `az login` first.' }
$subscriptionId = $account.id
Write-Host "Azure subscription: $($account.name) ($subscriptionId)"

# Rollout order: identity provider first, then the API (migrates the database on startup), then
# the PWA that calls both.
$images = @(
    Resolve-ImageReference -Namespace $ImageNamespace -Component keycloak -Version $KeycloakVersion
    Resolve-ImageReference -Namespace $ImageNamespace -Component api -Version $ApiVersion
    Resolve-ImageReference -Namespace $ImageNamespace -Component web -Version $WebVersion
)
foreach ($image in $images) {
    Write-Host ("Image {0,-8} {1}" -f $image.Component, $image.Reference)
    if (-not (Test-DockerHubTag -Namespace $image.Namespace -Repository $image.Repository -Tag $image.Tag)) {
        throw "Image $($image.Reference) does not exist on Docker Hub (published by ci.yml for latest, release.yml for X.Y.Z)."
    }
}
$byComponent = @{}
foreach ($image in $images) { $byComponent[$image.Component] = $image }

# --- 2. Infrastructure (Terraform) -------------------------------------------------------------

if ($AppsOnly) {
    $resourceGroup = "rg-$NamePrefix"
    $appNames = @{}
    foreach ($image in $images) { $appNames[$image.Component] = Get-ContainerAppName -NamePrefix $NamePrefix -Component $image.Component }
}
else {
    if (-not $StateStorageAccount) {
        # 3-24 lowercase alphanumerics, globally unique: stable per subscription.
        $StateStorageAccount = ('stbirrapointtf' + ($subscriptionId -replace '-', '').Substring(0, 10)).ToLower()
    }

    if ($PSCmdlet.ShouldProcess("state storage '$StateStorageAccount' in '$StateResourceGroup'", 'Ensure Terraform remote state')) {
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
    }

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
        "-var=api_image=$($byComponent.api.Reference)",
        "-var=web_image=$($byComponent.web.Reference)",
        "-var=keycloak_image=$($byComponent.keycloak.Reference)"
    )
    if ($AutoApprove) { $applyArgs += '-auto-approve' }
    if ($PSCmdlet.ShouldProcess('infra/terraform', 'terraform apply')) {
        Invoke-Native 'terraform apply' { terraform @applyArgs }
        $resourceGroup = (terraform -chdir="$terraformDir" output -raw resource_group_name).Trim()
        $appNames = (terraform -chdir="$terraformDir" output -json container_app_names) -join "`n" | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0) { throw 'Reading Terraform outputs failed.' }
    }
    else {
        # -WhatIf: nothing was applied, so preview the rollout against the default names.
        $resourceGroup = "rg-$NamePrefix"
        $appNames = @{}
        foreach ($image in $images) { $appNames[$image.Component] = Get-ContainerAppName -NamePrefix $NamePrefix -Component $image.Component }
    }
}

# --- 3. Image rollout ----------------------------------------------------------------------------

foreach ($image in $images) {
    $app = $appNames.($image.Component)
    $current = if ($WhatIfPreference) { $null } else {
        (Invoke-AzJson "Read $app image" {
            az containerapp show --name $app --resource-group $resourceGroup `
                --query 'properties.template.containers[0].image' --output json
        })
    }

    if (-not (Test-RolloutNeeded -Image $image -CurrentReference $current)) {
        Write-Host "==> $app already runs $($image.Reference); skipping." -ForegroundColor Cyan
        continue
    }

    $suffix = New-RevisionSuffix -Tag $image.Tag
    if ($PSCmdlet.ShouldProcess("$app ($resourceGroup)", "Roll out $($image.Reference) as revision suffix $suffix")) {
        Invoke-Native "Roll out $($image.Reference) to $app" {
            az containerapp update --name $app --resource-group $resourceGroup `
                --image $image.Reference --revision-suffix $suffix --output none
        }
        Wait-HealthyRevision -App $app -ResourceGroup $resourceGroup -TimeoutSeconds $RevisionTimeoutSeconds
    }
}

Write-Host ''
Write-Host 'Deployed.' -ForegroundColor Green
if (-not $AppsOnly -and -not $WhatIfPreference) { terraform -chdir="$terraformDir" output }
