<#
.SYNOPSIS
    Removes ABSOLUTELY EVERYTHING of the BirraPoint cloud environment - Azure and Neon, data
    included - so the next deploy starts from scratch (T143).

.DESCRIPTION
    Always a full wipe, no data is kept:
      1. `terraform init` against the HCP Terraform workspace, and a guard: the workspace must hold
         the requested environment (or be empty), otherwise nothing is changed;
      2. confirmation: type the resource group name (-Force skips it; -WhatIf changes nothing),
         then `terraform destroy` of everything Terraform manages (Container Apps, environment,
         Log Analytics, Key Vault - purged -, role assignments and the Neon project);
      3. sweep of whatever the destroy left behind, found by name: Log Analytics purge and
         `az group delete` of birrapoint-<environment>-rg, purge of the soft-deleted Key Vault,
         deletion of the Neon project of exactly that name (refused when several match);
      4. removal of the local infra/terraform/.terraform folder; final verification.
    Docker Hub images, GitHub secrets/variables and the HCP Terraform workspace itself are not
    touched.

    Idempotent: safe to re-run after a partial failure. Supports -WhatIf. Compatible with Windows
    PowerShell 5.1 and PowerShell 7.

    Prerequisites: Azure CLI logged in (`az login`), Terraform >= 1.9, HCP Terraform access
    (`terraform login` or TF_TOKEN_app_terraform_io) with TF_CLOUD_ORGANIZATION and TF_WORKSPACE
    set, NEON_API_KEY. The subscription is ARM_SUBSCRIPTION_ID or the `az login` default.

.EXAMPLE
    ./infra/teardown.ps1 -WhatIf
    Shows what would be removed and changes nothing.
.EXAMPLE
    ./infra/teardown.ps1
    Deletes everything, data included, after asking for confirmation.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    # Deployment environment (resources are named birrapoint-<environment>-<acronym>).
    [string] $Environment = 'PROD',

    # Defaults to infra/terraform/environments/<environment>.tfvars when it exists; Terraform also
    # loads the gitignored infra/terraform/terraform.tfvars on its own.
    [string] $VarFile,

    # Passed to Terraform only when given; otherwise tfvars or the variable default apply.
    [string] $Location,

    # Only when the Neon API key belongs to several organizations.
    [string] $NeonOrgId,

    # Skips the typed confirmation. For deliberate, scripted use only.
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$terraformDir = Join-Path $PSScriptRoot 'terraform'
Import-Module (Join-Path $PSScriptRoot 'Teardown.psm1') -Force
$environmentName = ConvertTo-EnvironmentName $Environment
# Resolved here, not as a param default: Windows PowerShell 5.1 leaves $PSScriptRoot empty in
# param defaults when the script is run with `powershell -File`.
if (-not $VarFile) {
    $defaultVarFile = Get-EnvironmentVarFile -TerraformDir $terraformDir -Environment $environmentName
    if (Test-Path $defaultVarFile) { $VarFile = $defaultVarFile }
}

$neonApi = 'https://console.neon.tech/api/v2'
$neonHeaders = @{ Authorization = "Bearer $env:NEON_API_KEY"; Accept = 'application/json' }

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

function Test-ResourceGroup([string] $Name) {
    $exists = (az group exists --name $Name) -join ''
    if ($LASTEXITCODE -ne 0) { throw "Checking resource group '$Name' failed." }
    $exists.Trim() -eq 'true'
}

function Find-NeonProjectByName([string] $Name) {
    $uri = "$neonApi/projects?limit=400&search=$([uri]::EscapeDataString($Name))"
    if ($NeonOrgId) { $uri += "&org_id=$([uri]::EscapeDataString($NeonOrgId))" }
    $response = Invoke-RestMethod -Uri $uri -Headers $neonHeaders -TimeoutSec 30
    Select-NeonProjectToDelete -Projects @($response.projects) -Name $Name
}

function Read-Confirmation([string] $Prompt, [string] $Expected) {
    $answer = Read-Host "$Prompt Type '$Expected' to continue"
    if (-not (Test-TeardownConfirmation -Expected $Expected -Answer $answer)) {
        throw 'Not confirmed; nothing was changed.'
    }
}

function Find-DeletedKeyVault([string] $Name) {
    $found = @(az keyvault list-deleted --resource-type vault --query "[?name=='$Name'].name" --output tsv)
    if ($LASTEXITCODE -ne 0) { throw 'Listing soft-deleted Key Vaults failed.' }
    @($found | Where-Object { $_ })
}

# --- 1. Prerequisites and what exists --------------------------------------------------------

foreach ($tool in 'az', 'terraform') { Assert-Command $tool }
if (-not $env:NEON_API_KEY) { throw 'NEON_API_KEY is not set (Neon console -> Account settings -> API keys).' }

$accountJson = (Invoke-Probe { az account show --output json }) -join "`n"
$account = if ($accountJson) { $accountJson | ConvertFrom-Json } else { $null }
if (-not $account) { throw 'Not logged in to Azure. Run `az login` first.' }

# terraform init is read-only for the infrastructure, so it also runs under -WhatIf: the guard
# below needs the workspace state before anything is shown or confirmed.
Invoke-Native 'terraform init' { terraform -chdir="$terraformDir" init -input=false }
$stateEnvironment = ((Invoke-Probe { terraform -chdir="$terraformDir" output -raw environment }) -join '').Trim()
if ($LASTEXITCODE -ne 0) { $stateEnvironment = '' }
$workspaceLabel = if ($env:TF_WORKSPACE) { $env:TF_WORKSPACE } else { '(default)' }
Write-Host ("HCP workspace {0} holds environment {1}" -f $workspaceLabel, $(if ($stateEnvironment) { $stateEnvironment } else { '(empty state)' }))
if (-not (Test-StateEnvironment -Expected $environmentName -StateEnvironment $stateEnvironment)) {
    throw "HCP workspace $workspaceLabel holds environment '$stateEnvironment', not '$environmentName'. Fix TF_WORKSPACE or -Environment; nothing was changed."
}

$appResourceGroup = Get-TeardownResourceName -Environment $environmentName -Resource ResourceGroup
$keyVault = Get-TeardownResourceName -Environment $environmentName -Resource KeyVault
$neonProjectName = Get-TeardownResourceName -Environment $environmentName -Resource NeonProject
$appExists = Test-ResourceGroup $appResourceGroup

$neonMatches = @(Find-NeonProjectByName $neonProjectName)
$neonTarget = Resolve-NeonProjectTarget -NameMatches $neonMatches
if ($neonTarget.Action -eq 'Refuse') {
    throw "Several Neon projects are named '$neonProjectName' ($(($neonMatches | ForEach-Object { $_.id }) -join ', ')). Delete the right one in the Neon console, or pass -NeonOrgId; nothing was changed."
}

Write-Host ''
Write-Host "Azure subscription: $($account.name) ($($account.id))"
Write-Host 'Will remove EVERYTHING:' -ForegroundColor Yellow
Write-Host ("  - {0} (Container Apps, environment, Log Analytics, Key Vault){1}" -f $appResourceGroup, $(if ($appExists) { '' } else { ' - already absent' }))
if ($neonTarget.Action -eq 'DeleteById') {
    Write-Host "  - Neon project $neonProjectName ($($neonTarget.ProjectId)) and ALL its data"
}
else {
    Write-Host "  - Neon project $neonProjectName - none found"
}
Write-Host '  - everything Terraform manages in the HCP Terraform workspace (terraform destroy)'
Write-Host '  - local infra/terraform/.terraform'
Write-Host ''

# --- 2. Confirmation -------------------------------------------------------------------------

if (-not $Force -and -not $WhatIfPreference) {
    # The operator types the resource group that is about to be deleted; its data is irreversibly lost.
    Read-Confirmation 'This DELETES ALL DATA of the environment listed above, irreversibly.' $appResourceGroup
}

$problems = @()

# --- 3. terraform destroy ---------------------------------------------------------------------

if ($PSCmdlet.ShouldProcess('infra/terraform', 'terraform destroy (everything)')) {
    try {
        $resolvedVarFile = if ($VarFile) { (Resolve-Path $VarFile).Path } else { $null }
        $destroyArgs = Get-DestroyArgument -TerraformDir $terraformDir -Environment $environmentName `
            -Location $Location -VarFile $resolvedVarFile
        Invoke-Native 'terraform destroy' { terraform @destroyArgs }
    }
    catch {
        # Keep going: the sweep below removes whatever the destroy left behind.
        Write-Warning "terraform destroy did not complete: $($_.Exception.Message)"
        $problems += 'terraform destroy failed; leftovers were swept directly.'
    }
}

# --- 4. Sweep leftovers -------------------------------------------------------------------------

if (Test-ResourceGroup $appResourceGroup) {
    if ($PSCmdlet.ShouldProcess($appResourceGroup, 'Purge Log Analytics workspaces and delete the resource group')) {
        # Purged rather than soft-deleted (14-day retention) so a redeploy never collides with it.
        $workspaces = @(az monitor log-analytics workspace list --resource-group $appResourceGroup --query '[].name' --output tsv)
        if ($LASTEXITCODE -ne 0) { throw "Listing Log Analytics workspaces in '$appResourceGroup' failed." }
        foreach ($workspace in $workspaces | Where-Object { $_ }) {
            Invoke-Native "Purge Log Analytics workspace '$workspace'" {
                az monitor log-analytics workspace delete --resource-group $appResourceGroup `
                    --workspace-name $workspace --force true --yes --output none
            }
        }
        Invoke-Native "Delete resource group '$appResourceGroup' (several minutes)" {
            az group delete --name $appResourceGroup --yes --output none
        }
    }
}

# A vault deleted with its resource group (not through Terraform, which purges it) stays
# soft-deleted for its retention period and blocks a redeploy under the same, globally unique name.
if ((Find-DeletedKeyVault $keyVault) -and $PSCmdlet.ShouldProcess($keyVault, 'Purge the soft-deleted Key Vault')) {
    Invoke-Native "Purge soft-deleted Key Vault '$keyVault'" {
        az keyvault purge --name $keyVault --output none
    }
}

# Only the project of exactly that name, and only if the destroy left it behind.
$neonLeft = @(Find-NeonProjectByName $neonProjectName)
if ($neonLeft.Count -gt 0) {
    $leftTarget = Resolve-NeonProjectTarget -NameMatches $neonLeft
    if ($leftTarget.Action -eq 'Refuse') {
        throw "Several Neon projects are named '$neonProjectName'; delete the right one in the Neon console."
    }
    if ($PSCmdlet.ShouldProcess("Neon project $($leftTarget.ProjectId)", 'Delete')) {
        Write-Host "==> Delete Neon project $($leftTarget.ProjectId)" -ForegroundColor Cyan
        Invoke-RestMethod -Method Delete -Uri "$neonApi/projects/$($leftTarget.ProjectId)" `
            -Headers $neonHeaders -TimeoutSec 60 | Out-Null
    }
}

$localTerraform = Join-Path $terraformDir '.terraform'
if ((Test-Path $localTerraform) -and $PSCmdlet.ShouldProcess($localTerraform, 'Remove')) {
    Remove-Item -Recurse -Force $localTerraform
}

# --- 5. Verify ------------------------------------------------------------------------------

Write-Host ''
if ($WhatIfPreference) {
    Write-Host 'What-if complete; nothing was changed.' -ForegroundColor Green
    return
}

$remaining = @()
if (Test-ResourceGroup $appResourceGroup) { $remaining += "resource group $appResourceGroup" }
if (Find-DeletedKeyVault $keyVault) { $remaining += "soft-deleted Key Vault $keyVault" }
foreach ($project in @(Find-NeonProjectByName $neonProjectName)) { $remaining += "Neon project $($project.id)" }

foreach ($problem in $problems) { Write-Warning $problem }
if ($remaining.Count -gt 0) {
    throw "Still present: $($remaining -join ', '). Re-run ./infra/teardown.ps1 to retry."
}

Write-Host 'Environment removed. The next deploy starts from scratch.' -ForegroundColor Green
