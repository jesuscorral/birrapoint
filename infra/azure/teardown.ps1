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
         deletion of the Neon project whose id is in the Terraform state (its name must be
         birrapoint-<environment>-neon; never chosen by name alone, see -NeonProjectId);
      4. removal of the local infra/azure/terraform/.terraform folder; final verification.
    Docker Hub images, GitHub secrets/variables and the HCP Terraform workspace itself are not
    touched.

    Idempotent: safe to re-run after a partial failure. Supports -WhatIf. Compatible with Windows
    PowerShell 5.1 and PowerShell 7.

    Prerequisites: Azure CLI logged in (`az login`), Terraform >= 1.9, HCP Terraform access
    (`terraform login` or TF_TOKEN_app_terraform_io) with TF_CLOUD_ORGANIZATION and TF_WORKSPACE
    set, NEON_API_KEY. The subscription is ARM_SUBSCRIPTION_ID or the `az login` default.

.EXAMPLE
    ./infra/azure/teardown.ps1 -WhatIf
    Shows what would be removed and changes nothing.
.EXAMPLE
    ./infra/azure/teardown.ps1
    Deletes everything, data included, after asking for confirmation.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    # Deployment environment (resources are named birrapoint-<environment>-<acronym>).
    [string] $Environment = 'PROD',

    # Defaults to infra/azure/terraform/environments/<environment>.tfvars when it exists; Terraform also
    # loads the gitignored infra/azure/terraform/terraform.tfvars on its own.
    [string] $VarFile,

    # Passed to Terraform only when given; otherwise tfvars or the variable default apply.
    [string] $Location,

    # Neon organization id. Defaults to TF_VAR_neon_org_id, then to the API key's only organization.
    [string] $NeonOrgId,

    # Neon project id to delete when the Terraform state is empty (e.g. after a destroy that failed
    # half way). Without it an empty state deletes no Neon project: name matches are only reported.
    # The project's name must still be birrapoint-<environment>-neon.
    [ValidatePattern('^[a-z0-9-]*$')]
    [string] $NeonProjectId,

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

$script:StateNeonProjectId = ''
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
        throw "Prerequisite '$Name' is not on PATH (see infra/azure/terraform/README.md)."
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

function Get-NeonOrganization {
    # The API key's organizations. An organization-scoped key cannot list them; it gets none here
    # and then needs -NeonOrgId or TF_VAR_neon_org_id when Neon asks for org_id.
    try {
        $response = Invoke-RestMethod -Uri "$neonApi/users/me/organizations" -Headers $neonHeaders -TimeoutSec 30
        @($response.organizations)
    }
    catch {
        @()
    }
}

function Get-NeonProjectById([string] $Id) {
    # The project with that id, or $null when Neon answers 404; any other failure throws.
    try {
        $response = Invoke-RestMethod -Uri "$neonApi/projects/$([uri]::EscapeDataString($Id))" -Headers $neonHeaders -TimeoutSec 30
        $response.project
    }
    catch {
        $status = $null
        if ($_.Exception.Response) { $status = [int] $_.Exception.Response.StatusCode }
        if ($status -eq 404) { return $null }
        throw
    }
}

function Get-NeonDeletionTarget {
    # What the teardown may do with Neon right now (see Resolve-NeonDeletionTarget): the project id
    # comes from the Terraform state or -NeonProjectId, its name is verified against the Neon API,
    # and a name match alone never selects anything (the Neon account is shared with the AWS
    # deployment).
    $uri = "$neonApi/projects?limit=400&search=$([uri]::EscapeDataString($neonProjectName))"
    if ($NeonOrgId) { $uri += "&org_id=$([uri]::EscapeDataString($NeonOrgId))" }
    $found = @((Invoke-RestMethod -Uri $uri -Headers $neonHeaders -TimeoutSec 30).projects)
    $targetId = if ($script:StateNeonProjectId) { $script:StateNeonProjectId } else { $NeonProjectId }
    if ($targetId -and -not ($found | Where-Object { $_.id -ceq $targetId })) {
        $byId = Get-NeonProjectById $targetId
        if ($byId) { $found += $byId }
    }
    Resolve-NeonDeletionTarget -StateProjectId $script:StateNeonProjectId -ExplicitProjectId $NeonProjectId `
        -ProjectsFound $found -ExpectedName $neonProjectName
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
$stateList = @(Invoke-Probe { terraform -chdir="$terraformDir" state list })
$stateListExit = $LASTEXITCODE
$stateOutput = ''
$outputExit = 0
if ($stateListExit -eq 0 -and @($stateList | Where-Object { $_ }).Count -gt 0) {
    $stateOutput = (Invoke-Probe { terraform -chdir="$terraformDir" output -raw environment }) -join ''
    $outputExit = $LASTEXITCODE
}
$stateEnvironment = Resolve-StateEnvironment -StateListExitCode $stateListExit -StateList $stateList `
    -OutputExitCode $outputExit -Output $stateOutput
$workspaceLabel = if ($env:TF_WORKSPACE) { $env:TF_WORKSPACE } else { '(default)' }
Write-Host ("HCP workspace {0} holds environment {1}" -f $workspaceLabel, $(if ($stateEnvironment) { $stateEnvironment } else { '(empty state)' }))
if (-not (Test-StateEnvironment -Expected $environmentName -StateEnvironment $stateEnvironment)) {
    throw "HCP workspace $workspaceLabel holds environment '$stateEnvironment', not '$environmentName'. Fix TF_WORKSPACE or -Environment; nothing was changed."
}

# The Neon project of THIS deployment, by id: the account is shared with the AWS deployment, so a
# project is never chosen by name alone. An unreadable output (e.g. the project is already gone from
# the state) just leaves the id empty: then nothing is deleted without -NeonProjectId.
if ($stateEnvironment) {
    $stateNeonOutput = (Invoke-Probe { terraform -chdir="$terraformDir" output -raw neon_project_id }) -join ''
    if ($LASTEXITCODE -eq 0 -and $stateNeonOutput.Trim()) { $script:StateNeonProjectId = $stateNeonOutput.Trim() }
}

$appResourceGroup = Get-TeardownResourceName -Environment $environmentName -Resource ResourceGroup
$keyVault = Get-TeardownResourceName -Environment $environmentName -Resource KeyVault
$neonProjectName = Get-TeardownResourceName -Environment $environmentName -Resource NeonProject
$appExists = Test-ResourceGroup $appResourceGroup

if (-not $NeonOrgId -and -not $env:TF_VAR_neon_org_id) {
    $NeonOrgId = Resolve-NeonOrgId -Organizations (Get-NeonOrganization)
}
else {
    $NeonOrgId = Resolve-NeonOrgId -Explicit $NeonOrgId -EnvValue $env:TF_VAR_neon_org_id
}
if ($NeonOrgId) { Write-Host "Neon organization $NeonOrgId" }

$neonTarget = Get-NeonDeletionTarget
if ($neonTarget.Action -eq 'Refuse') {
    throw "$($neonTarget.Reason) Nothing was changed."
}

Write-Host ''
Write-Host "Azure subscription: $($account.name) ($($account.id))"
Write-Host 'Will remove EVERYTHING:' -ForegroundColor Yellow
Write-Host ("  - {0} (Container Apps, environment, Log Analytics, Key Vault){1}" -f $appResourceGroup, $(if ($appExists) { '' } else { ' - already absent' }))
if ($neonTarget.Action -eq 'DeleteById') {
    Write-Host "  - Neon project $neonProjectName ($($neonTarget.ProjectId)) and ALL its data"
}
elseif ($neonTarget.Action -eq 'ReportOnly') {
    Write-Host "  - Neon project: NOT deleted. $($neonTarget.Reason) Found: $((@($neonTarget.Candidates) | ForEach-Object { $_.id }) -join ', ')" -ForegroundColor Yellow
}
else {
    Write-Host "  - Neon project $neonProjectName - none to delete"
}
Write-Host '  - everything Terraform manages in the HCP Terraform workspace (terraform destroy)'
Write-Host '  - local infra/azure/terraform/.terraform'
Write-Host ''

# --- 2. Confirmation -------------------------------------------------------------------------

if (-not $Force -and -not $WhatIfPreference) {
    # The operator types the resource group that is about to be deleted; its data is irreversibly lost.
    Read-Confirmation 'This DELETES ALL DATA of the environment listed above, irreversibly.' $appResourceGroup
}

$problems = @()

# --- 3. terraform destroy ---------------------------------------------------------------------

if ($PSCmdlet.ShouldProcess('infra/azure/terraform', 'terraform destroy (everything)')) {
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

# Only the project id of the state (or -NeonProjectId), verified by name, and only if the destroy
# left it behind. Never by name alone.
$leftTarget = Get-NeonDeletionTarget
if ($leftTarget.Action -eq 'Refuse') { throw $leftTarget.Reason }
if ($leftTarget.Action -eq 'ReportOnly') { Write-Warning $leftTarget.Reason }
if ($leftTarget.Action -eq 'DeleteById' -and $PSCmdlet.ShouldProcess("Neon project $($leftTarget.ProjectId)", 'Delete')) {
    Write-Host "==> Delete Neon project $($leftTarget.ProjectId)" -ForegroundColor Cyan
    Invoke-RestMethod -Method Delete -Uri "$neonApi/projects/$([uri]::EscapeDataString($leftTarget.ProjectId))" `
        -Headers $neonHeaders -TimeoutSec 60 | Out-Null
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
# Verified by id: a name match alone proves nothing (the AWS deployment shares the Neon account).
$neonId = if ($script:StateNeonProjectId) { $script:StateNeonProjectId } else { $NeonProjectId }
if ($neonId -and (Get-NeonProjectById $neonId)) { $remaining += "Neon project $neonId" }

foreach ($problem in $problems) { Write-Warning $problem }
if ($remaining.Count -gt 0) {
    throw "Still present: $($remaining -join ', '). Re-run ./infra/azure/teardown.ps1 to retry."
}

Write-Host 'Environment removed. The next deploy starts from scratch.' -ForegroundColor Green
