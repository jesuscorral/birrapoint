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
         images only when it first creates a Container App (lifecycle ignore_changes, T134), and
         gets a fresh revision suffix per apply, so every full run creates a new revision of each
         app (a restart) and never reuses a suffix left by an earlier rollout (ADR-0018).
      3. Rolls each app to its image with `az containerapp update` - keycloak, then api (which
         migrates the database on startup), then web - waiting for each new revision to become
         healthy before moving on. An app whose latest revision is healthy and already serves
         the target release is skipped (so is `latest` right after the apply pulled it).

    -AppsOnly is the image-only deployment used by the deploy pipeline (deploy.yml): no Terraform,
    tfvars, NEON_API_KEY or state access - only `az login` with rights on the application
    resource group. Compatible with Windows PowerShell 5.1 and PowerShell 7. -WhatIf reads Azure
    and Docker Hub but changes nothing (no Terraform init/apply, no rollout).

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

function Get-TerraformOutput {
    param([string] $Name, [switch] $Json)
    $format = if ($Json) { '-json' } else { '-raw' }
    $value = (terraform -chdir="$terraformDir" output $format $Name) -join "`n"
    if ($LASTEXITCODE -ne 0 -or -not $value) { throw "Reading Terraform output '$Name' failed." }
    if ($Json) { $value | ConvertFrom-Json } else { $value.Trim() }
}

function Get-AppState {
    # Latest / latest-ready revision, the ready revision's image and the app's minimum replicas;
    # $null only when the resource group or the Container App does not exist (yet) - any other
    # failure (throttling, expired login, wrong subscription) throws instead of passing for
    # "not found".
    param([string] $App, [string] $ResourceGroup)
    $groupExists = (az group exists --name $ResourceGroup) -join ''
    if ($LASTEXITCODE -ne 0) { throw "Checking resource group '$ResourceGroup' failed." }
    if ($groupExists.Trim() -ne 'true') { return $null }

    # `list` + filter instead of `show`: a missing app is an empty result, not an error.
    $json = (az containerapp list --resource-group $ResourceGroup --output json `
            --query "[?name=='$App'] | [0].{latest: properties.latestRevisionName, ready: properties.latestReadyRevisionName, minReplicas: properties.template.scale.minReplicas}") -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Reading Container App '$App' failed." }
    if (-not $json -or $json.Trim() -eq 'null') { return $null }
    $state = $json | ConvertFrom-Json

    $readyImage = $null
    if ($state.ready) {
        $readyImage = (az containerapp revision show --name $App --resource-group $ResourceGroup `
                --revision $state.ready --query 'properties.template.containers[0].image' --output tsv) -join ''
        if ($LASTEXITCODE -ne 0) { throw "Reading revision $($state.ready) of $App failed." }
    }
    [pscustomobject]@{
        Latest      = $state.latest
        Ready       = $state.ready
        ReadyImage  = if ($readyImage) { $readyImage.Trim() } else { $null }
        # Unset means the platform default (not zero): never treat it as "may scale to zero".
        MinReplicas = if ($null -ne $state.minReplicas) { [int] $state.minReplicas } else { 1 }
    }
}

function Wait-HealthyRevision {
    # Polls the revision this run created (not "whatever is latest"); tolerates a few transient az
    # failures in a row before giving up.
    param([string] $App, [string] $ResourceGroup, [string] $Revision, [int] $MinReplicas, [int] $TimeoutSeconds)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $consecutiveErrors = 0
    # A slow start (e.g. Keycloak booting behind the default TCP probe) can pass through Degraded;
    # only a Degraded state that persists for a minute fails the deployment.
    $consecutiveDegraded = 0
    while ($true) {
        try {
            $revision = Invoke-AzJson "Read revision $Revision" {
                az containerapp revision show --name $App --resource-group $ResourceGroup --revision $Revision --output json `
                    --query '{provisioning: properties.provisioningState, running: properties.runningState, health: properties.healthState, replicas: properties.replicas}'
            }
            $consecutiveErrors = 0
        }
        catch {
            $consecutiveErrors++
            if ($consecutiveErrors -ge 3) { throw }
            Write-Host "    transient error reading $Revision ($consecutiveErrors/3): $($_.Exception.Message)" -ForegroundColor Yellow
            Start-Sleep -Seconds 10
            continue
        }

        $replicas = if ($null -ne $revision.replicas) { [int] $revision.replicas } else { 0 }
        $outcome = Get-RevisionOutcome -ProvisioningState $revision.provisioning -RunningState $revision.running `
            -HealthState $revision.health -Replicas $replicas -MinReplicas $MinReplicas
        Write-Host ("    {0}: provisioning={1} running={2} health={3} replicas={4} -> {5}" -f
            $Revision, $revision.provisioning, $revision.running, $revision.health, $replicas, $outcome)

        if ($outcome -eq 'Succeeded') { return }
        $consecutiveDegraded = if ($outcome -eq 'Degraded') { $consecutiveDegraded + 1 } else { 0 }
        if ($outcome -eq 'Failed' -or $consecutiveDegraded -ge 6) {
            throw "Revision $Revision failed (provisioning=$($revision.provisioning), running=$($revision.running)). Check its logs; the previous revision keeps serving until a healthy one replaces it."
        }
        if ((Get-Date) -gt $deadline) {
            throw "Revision $Revision did not become healthy within $TimeoutSeconds s."
        }
        Start-Sleep -Seconds 10
    }
}

# `az containerapp` subcommands outside the core CLI install their extension without prompting
# (non-interactive runs in CI); restored on exit.
$previousDynamicInstall = $env:AZURE_EXTENSION_USE_DYNAMIC_INSTALL
$env:AZURE_EXTENSION_USE_DYNAMIC_INSTALL = 'yes_without_prompt'
try {

    # --- 1. Prerequisites and images ---------------------------------------------------------

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

    # Rollout order: identity provider first, then the API (migrates the database on startup),
    # then the PWA that calls both.
    $images = @(
        Resolve-ImageReference -Namespace $ImageNamespace -Component keycloak -Version $KeycloakVersion
        Resolve-ImageReference -Namespace $ImageNamespace -Component api -Version $ApiVersion
        Resolve-ImageReference -Namespace $ImageNamespace -Component web -Version $WebVersion
    )
    foreach ($image in $images) {
        Write-Host ("Image {0,-9} {1}" -f $image.Component, $image.Reference)
        if (-not (Test-DockerHubTag -Namespace $image.Namespace -Repository $image.Repository -Tag $image.Tag)) {
            throw "Image $($image.Reference) does not exist on Docker Hub (published by ci.yml for latest, release.yml for X.Y.Z)."
        }
    }
    $byComponent = @{}
    foreach ($image in $images) { $byComponent[$image.Component] = $image }

    # Default names (-AppsOnly, -WhatIf); a real apply reads them back from Terraform's outputs.
    $resourceGroup = "rg-$NamePrefix"
    $appNames = @{}
    foreach ($image in $images) { $appNames[$image.Component] = Get-ContainerAppName -NamePrefix $NamePrefix -Component $image.Component }

    # --- 2. Infrastructure (Terraform) ---------------------------------------------------------

    $infraSuffix = $null
    $applyInfrastructure = -not $AppsOnly -and
        $PSCmdlet.ShouldProcess('infra/terraform (remote state bootstrap, init, apply)', 'Apply infrastructure')
    if (-not $AppsOnly -and -not $applyInfrastructure -and -not $WhatIfPreference) {
        throw 'Infrastructure apply declined; nothing was changed. Use -AppsOnly to roll out images without Terraform.'
    }
    if ($applyInfrastructure) {
        if (-not $StateStorageAccount) {
            # 3-24 lowercase alphanumerics, globally unique: stable per subscription.
            $StateStorageAccount = ('stbirrapointtf' + ($subscriptionId -replace '-', '').Substring(0, 10)).ToLower()
        }

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

        Invoke-Native 'terraform init' {
            terraform -chdir="$terraformDir" init -input=false -reconfigure `
                "-backend-config=resource_group_name=$StateResourceGroup" `
                "-backend-config=storage_account_name=$StateStorageAccount" `
                "-backend-config=container_name=$StateContainer" `
                "-backend-config=key=$StateKey"
        }

        # An app whose latest revision is not the serving one (a failed earlier rollout) keeps the
        # failed image in its template, and the apply below would start a new revision from it.
        # Recover such apps with -AppsOnly first. New environments have no outputs yet: skipped.
        $existingApps = $null
        $outputsJson = (Invoke-Probe { terraform -chdir="$terraformDir" output -json container_app_names }) -join "`n"
        if ($outputsJson -and $outputsJson.Trim().StartsWith('{')) {
            $existingApps = $outputsJson | ConvertFrom-Json
            $existingGroup = Get-TerraformOutput 'resource_group_name'
            foreach ($component in 'keycloak', 'api', 'web') {
                $existing = Get-AppState -App $existingApps.$component -ResourceGroup $existingGroup
                if ($existing -and $existing.Latest -ne $existing.Ready) {
                    throw "Container App '$($existingApps.$component)' has a latest revision ($($existing.Latest)) that is not the serving one ($($existing.Ready)), e.g. after a failed rollout. Recover it first: ./infra/deploy.ps1 -AppsOnly -ImageNamespace $ImageNamespace -<Component>Version <good version>."
                }
            }
        }

        # A fresh suffix per apply: every apply creates a new revision of each app (a restart),
        # and never reuses a suffix a previous rollout left in state (ADR-0018). Its length
        # against the real name_prefix is validated by Terraform.
        $infraSuffix = New-RevisionSuffix -Tag 'infra'
        $applyArgs = @(
            "-chdir=$terraformDir", 'apply', '-input=false',
            "-var-file=$((Resolve-Path $VarFile).Path)",
            "-var=subscription_id=$subscriptionId",
            "-var=location=$Location",
            "-var=api_image=$($byComponent.api.Reference)",
            "-var=web_image=$($byComponent.web.Reference)",
            "-var=keycloak_image=$($byComponent.keycloak.Reference)",
            "-var=revision_suffix=$infraSuffix"
        )
        if ($AutoApprove) { $applyArgs += '-auto-approve' }
        Invoke-Native 'terraform apply' { terraform @applyArgs }

        $resourceGroup = Get-TerraformOutput 'resource_group_name'
        $appNames = Get-TerraformOutput 'container_app_names' -Json
    }

    # --- 3. Image rollout ------------------------------------------------------------------------

    foreach ($image in $images) {
        $app = $appNames.($image.Component)
        $state = Get-AppState -App $app -ResourceGroup $resourceGroup
        if (-not $state -and -not $WhatIfPreference) {
            throw "Container App '$app' not found in '$resourceGroup'. Create the environment first with a full run (without -AppsOnly)."
        }

        if ($state) {
            $needed = Test-RolloutNeeded -Image $image -ReadyImage $state.ReadyImage `
                -LatestIsReady:($state.Latest -and $state.Latest -eq $state.Ready) `
                -JustRefreshed:($infraSuffix -and $state.Latest -eq "$app--$infraSuffix")
            if (-not $needed) {
                Write-Host "==> $app already serves $($image.Reference) from a healthy revision; skipping." -ForegroundColor Cyan
                continue
            }
        }

        $suffix = New-RevisionSuffix -Tag $image.Tag -AppName $app
        if ($PSCmdlet.ShouldProcess("$app ($resourceGroup)", "Roll out $($image.Reference) as revision $app--$suffix")) {
            Invoke-Native "Roll out $($image.Reference) to $app" {
                az containerapp update --name $app --resource-group $resourceGroup `
                    --image $image.Reference --revision-suffix $suffix --output none
            }
            Wait-HealthyRevision -App $app -ResourceGroup $resourceGroup -Revision "$app--$suffix" `
                -MinReplicas $state.MinReplicas -TimeoutSeconds $RevisionTimeoutSeconds
        }
    }

    Write-Host ''
    if ($WhatIfPreference) {
        Write-Host 'What-if complete; nothing was changed.' -ForegroundColor Green
    }
    else {
        Write-Host 'Deployed.' -ForegroundColor Green
        if (-not $AppsOnly) { terraform -chdir="$terraformDir" output }
    }
}
finally {
    $env:AZURE_EXTENSION_USE_DYNAMIC_INSTALL = $previousDynamicInstall
}
