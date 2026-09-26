<#
.SYNOPSIS
    Removes the BirraPoint cloud environment so it costs nothing, leaving everything ready for a
    clean `infra/deploy.ps1` run (T140).

.DESCRIPTION
    Default: removes only what Azure bills for - the application resource group rg-<NamePrefix>
    with its three Container Apps, the Container Apps environment and Log Analytics - through
    `terraform destroy -target=azurerm_resource_group.main`. It KEEPS:
      - the Neon project and its data (free plan; its compute suspends on its own when idle);
      - the Terraform state (rg-birrapoint-tfstate, a few KB): it remembers the Neon project
        and the generated passwords - among them the API admin-client secret already stored
        in Keycloak's database - so the next deploy.ps1 recreates only the Azure part and
        reconnects to the same database with matching secrets;
      - Docker Hub images, GitHub secrets and the deploy identity.

    -IncludeNeon: full wipe for a fresh start with empty data - destroys everything Terraform
    manages (Neon project included), deletes the state resource group and the local .terraform
    folder. Asks for a second confirmation because production data is deleted irreversibly.

    Idempotent: safe to re-run after a partial failure. If `terraform destroy` fails or the state
    is missing, leftovers are swept directly (az group delete, and with -IncludeNeon the Neon
    project of exactly that name via the Neon API). Supports -WhatIf. Compatible with Windows
    PowerShell 5.1 and PowerShell 7.

    Prerequisites: Azure CLI logged in (`az login`), Terraform >= 1.9, NEON_API_KEY.

.EXAMPLE
    ./infra/teardown.ps1
    Stops all Azure costs; keeps Neon and the state for a redeploy with the same data.
.EXAMPLE
    ./infra/teardown.ps1 -WhatIf
    Shows what would be removed and changes nothing.
.EXAMPLE
    ./infra/teardown.ps1 -IncludeNeon
    Deletes everything, data included.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    # Fallback only: the resource group and the Neon project are read from the Terraform state.
    # Used when there is no state (must then match Terraform's name_prefix).
    [string] $NamePrefix = 'birrapoint',

    # Defaults to infra/terraform/terraform.tfvars when it exists.
    [string] $VarFile,

    # Passed to Terraform only when given; otherwise tfvars or the variable default apply.
    [string] $Location,
    [string] $StateResourceGroup = 'rg-birrapoint-tfstate',
    # Globally unique; derived from the subscription id when omitted (same rule as deploy.ps1).
    [string] $StateStorageAccount,
    [string] $StateContainer = 'tfstate',
    [string] $StateKey = 'birrapoint.tfstate',

    # -IncludeNeon without a state only, when the Neon API key belongs to several organizations.
    [string] $NeonOrgId,

    [switch] $IncludeNeon,

    # Skips the typed confirmations. For deliberate, scripted use only.
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$terraformDir = Join-Path $PSScriptRoot 'terraform'
# Resolved here, not as a param default: Windows PowerShell 5.1 leaves $PSScriptRoot empty in
# param defaults when the script is run with `powershell -File`.
if (-not $VarFile) {
    $defaultVarFile = Join-Path $terraformDir 'terraform.tfvars'
    if (Test-Path $defaultVarFile) { $VarFile = $defaultVarFile }
}
Import-Module (Join-Path $PSScriptRoot 'Teardown.psm1') -Force

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

function Get-StateJson {
    # The raw state document straight from its blob - read-only (no `terraform init`), so it also
    # works under -WhatIf and before the confirmation. $null when there is no state.
    if (-not (Test-ResourceGroup $StateResourceGroup)) { return $null }
    $found = az storage account list --resource-group $StateResourceGroup `
        --query "[?name=='$StateStorageAccount'].name" --output tsv
    if ($LASTEXITCODE -ne 0) { throw 'Listing state storage accounts failed.' }
    if (-not $found) { return $null }

    $key = az storage account keys list --account-name $StateStorageAccount --resource-group $StateResourceGroup `
        --query '[0].value' --output tsv
    if ($LASTEXITCODE -ne 0 -or -not $key) { throw 'Reading the state storage account key failed.' }

    # The key goes through the environment (read by az storage), never on a command line;
    # whatever the operator had in those variables is restored afterwards.
    $file = [IO.Path]::GetTempFileName()
    $previousStorage = @($env:AZURE_STORAGE_ACCOUNT, $env:AZURE_STORAGE_KEY)
    $env:AZURE_STORAGE_ACCOUNT = $StateStorageAccount
    $env:AZURE_STORAGE_KEY = $key
    try {
        $blobExists = (az storage blob exists --container-name $StateContainer --name $StateKey `
                --query exists --output tsv) -join ''
        if ($LASTEXITCODE -ne 0) { throw 'Checking the state blob failed.' }
        if ($blobExists.Trim() -ne 'true') { return $null }

        az storage blob download --container-name $StateContainer --name $StateKey --file $file --overwrite `
            --no-progress --output none
        if ($LASTEXITCODE -ne 0) { throw 'Downloading the Terraform state failed.' }
        [IO.File]::ReadAllText($file)
    }
    finally {
        $env:AZURE_STORAGE_ACCOUNT = $previousStorage[0]
        $env:AZURE_STORAGE_KEY = $previousStorage[1]
        # A local temp file, never part of the what-if: always removed.
        Remove-Item $file -Force -ErrorAction SilentlyContinue -WhatIf:$false
    }
}

function Test-NeonProject([string] $ProjectId) {
    try {
        Invoke-RestMethod -Uri "$neonApi/projects/$ProjectId" -Headers $neonHeaders -TimeoutSec 30 | Out-Null
        return $true
    }
    catch {
        $response = $_.Exception.Response
        if ($response -and [int] $response.StatusCode -eq 404) { return $false }
        throw
    }
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

# --- 1. Prerequisites and what exists --------------------------------------------------------

foreach ($tool in 'az', 'terraform') { Assert-Command $tool }
if (-not $env:NEON_API_KEY) { throw 'NEON_API_KEY is not set (Neon console -> Account settings -> API keys).' }

$accountJson = (Invoke-Probe { az account show --output json }) -join "`n"
$account = if ($accountJson) { $accountJson | ConvertFrom-Json } else { $null }
if (-not $account) { throw 'Not logged in to Azure. Run `az login` first.' }
$subscriptionId = $account.id
if (-not $StateStorageAccount) {
    # 3-24 lowercase alphanumerics, globally unique: stable per subscription.
    $StateStorageAccount = ('stbirrapointtf' + ($subscriptionId -replace '-', '').Substring(0, 10)).ToLower()
}

# Names come from the state (what Terraform actually created), not from parameters.
$stateJson = Get-StateJson
$stateExists = [bool] $stateJson
$outputs = Read-StateOutput -StateJson $stateJson
$appResourceGroup = if ($outputs.resource_group_name) { $outputs.resource_group_name } else { "rg-$NamePrefix" }
$appExists = Test-ResourceGroup $appResourceGroup

$neonTarget = $null
if ($IncludeNeon) {
    $nameMatches = if ($outputs.neon_project_id) { @() } else { @(Find-NeonProjectByName $NamePrefix) }
    $neonTarget = Resolve-NeonProjectTarget -StateProjectId $outputs.neon_project_id -NameMatches $nameMatches
    if ($neonTarget.Action -eq 'Refuse') {
        throw "Several Neon projects are named '$NamePrefix' ($(($nameMatches | ForEach-Object { $_.id }) -join ', ')) and there is no Terraform state to tell which one is BirraPoint's. Delete the right one in the Neon console; nothing was changed."
    }
}

Write-Host ''
Write-Host "Azure subscription: $($account.name) ($subscriptionId)"
Write-Host ("Terraform state: {0}" -f $(if ($stateExists) { "$StateResourceGroup/$StateStorageAccount" } else { 'none found' }))
Write-Host 'Will remove:' -ForegroundColor Yellow
Write-Host ("  - {0} (Container Apps, environment, Log Analytics){1}" -f $appResourceGroup, $(if ($appExists) { '' } else { ' - already absent' }))
if ($IncludeNeon) {
    if ($neonTarget.Action -eq 'DeleteById') {
        Write-Host "  - Neon project $($neonTarget.ProjectId) (from the $($neonTarget.Source)) and ALL its data"
    }
    else {
        Write-Host "  - Neon project: none found"
    }
    Write-Host ("  - Terraform state: {0}{1}" -f $StateResourceGroup, $(if ($stateExists) { '' } else { ' - already absent' }))
    Write-Host '  - local infra/terraform/.terraform'
}
else {
    Write-Host 'Will keep: Neon project and data, Terraform state, Docker Hub images, GitHub secrets.' -ForegroundColor Green
}
Write-Host ''

# --- 2. Confirmation -------------------------------------------------------------------------

if (-not $Force -and -not $WhatIfPreference) {
    # The operator types the resource group that is actually about to be deleted.
    Read-Confirmation 'This removes the environment listed above.' $appResourceGroup
    if ($IncludeNeon) {
        Read-Confirmation 'This ALSO DELETES ALL PRODUCTION DATA in Neon, irreversibly.' 'delete data'
    }
}

$problems = @()

# --- 3. terraform destroy (when the state exists) ---------------------------------------------

if ($stateExists) {
    if ($PSCmdlet.ShouldProcess('infra/terraform', "terraform destroy ($(if ($IncludeNeon) { 'everything' } else { "target $appResourceGroup" }))")) {
        try {
            Invoke-Native 'terraform init' {
                terraform -chdir="$terraformDir" init -input=false -reconfigure `
                    "-backend-config=resource_group_name=$StateResourceGroup" `
                    "-backend-config=storage_account_name=$StateStorageAccount" `
                    "-backend-config=container_name=$StateContainer" `
                    "-backend-config=key=$StateKey"
            }
            $resolvedVarFile = if ($VarFile) { (Resolve-Path $VarFile).Path } else { $null }
            $destroyArgs = Get-DestroyArgument -TerraformDir $terraformDir -SubscriptionId $subscriptionId `
                -Location $Location -VarFile $resolvedVarFile -IncludeNeon:$IncludeNeon
            Invoke-Native 'terraform destroy' { terraform @destroyArgs }
        }
        catch {
            # Keep going: the sweep below removes whatever the destroy left behind.
            Write-Warning "terraform destroy did not complete: $($_.Exception.Message)"
            $problems += 'terraform destroy failed; leftovers were swept directly.'
        }
    }
}
else {
    Write-Host "No Terraform state found; sweeping resources directly." -ForegroundColor Yellow
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

if ($IncludeNeon) {
    # Only the project identified above (by its id), and only if the destroy left it behind.
    if ($neonTarget.Action -eq 'DeleteById' -and (Test-NeonProject $neonTarget.ProjectId)) {
        if ($PSCmdlet.ShouldProcess("Neon project $($neonTarget.ProjectId)", 'Delete')) {
            Write-Host "==> Delete Neon project $($neonTarget.ProjectId)" -ForegroundColor Cyan
            Invoke-RestMethod -Method Delete -Uri "$neonApi/projects/$($neonTarget.ProjectId)" `
                -Headers $neonHeaders -TimeoutSec 60 | Out-Null
        }
    }

    if ((Test-ResourceGroup $StateResourceGroup) -and $PSCmdlet.ShouldProcess($StateResourceGroup, 'Delete the Terraform state resource group')) {
        Invoke-Native "Delete resource group '$StateResourceGroup'" {
            az group delete --name $StateResourceGroup --yes --output none
        }
    }

    $localTerraform = Join-Path $terraformDir '.terraform'
    if ((Test-Path $localTerraform) -and $PSCmdlet.ShouldProcess($localTerraform, 'Remove')) {
        Remove-Item -Recurse -Force $localTerraform
    }
}

# --- 5. Verify ------------------------------------------------------------------------------

Write-Host ''
if ($WhatIfPreference) {
    Write-Host 'What-if complete; nothing was changed.' -ForegroundColor Green
    return
}

$remaining = @()
if (Test-ResourceGroup $appResourceGroup) { $remaining += "resource group $appResourceGroup" }
if ($IncludeNeon) {
    if (Test-ResourceGroup $StateResourceGroup) { $remaining += "resource group $StateResourceGroup" }
    if ($neonTarget.Action -eq 'DeleteById' -and (Test-NeonProject $neonTarget.ProjectId)) {
        $remaining += "Neon project $($neonTarget.ProjectId)"
    }
}

foreach ($problem in $problems) { Write-Warning $problem }
if ($remaining.Count -gt 0) {
    throw "Still present: $($remaining -join ', '). Re-run ./infra/teardown.ps1 to retry."
}

Write-Host 'Environment removed.' -ForegroundColor Green
if (-not $IncludeNeon) {
    Write-Host 'Kept: Neon project + data and the Terraform state. Redeploy with ./infra/deploy.ps1.'
}
