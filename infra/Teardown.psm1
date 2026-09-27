# Pure helpers behind infra/teardown.ps1 (T140): the confirmation check, which Neon projects may
# be deleted, and the `terraform destroy` arguments. Kept free of Azure/Neon calls so they are
# unit-testable (infra/tests/Teardown.Tests.ps1). Windows PowerShell 5.1 and PowerShell 7.

$ErrorActionPreference = 'Stop'

function Test-TeardownConfirmation {
    # The operator must type the exact expected word (the resource group name); anything else - including
    # "yes" or a different casing - aborts.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Expected,
        [AllowNull()] [AllowEmptyString()] [string] $Answer
    )
    if ($null -eq $Answer) { return $false }
    $Answer.Trim() -ceq $Expected
}

function Select-NeonProjectToDelete {
    # Only projects whose name is exactly the deployment's Neon project name; never a prefix or
    # substring match, so no other Neon project of the account is ever selected.
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()] [object[]] $Projects = @(),
        [Parameter(Mandatory = $true)] [string] $Name
    )
    @($Projects | Where-Object { $_.name -ceq $Name })
}

function Get-DestroyArgument {
    # `terraform destroy` arguments. By default only the application resource group - and with
    # it every Container App, the environment, Log Analytics and the Key Vault - is targeted: the Neon project
    # and the generated passwords stay in the state, so the next deploy reconnects to the same
    # database with matching secrets. -IncludeNeon destroys everything.
    # The var file comes first so the -var flags win, as in deploy.ps1. The per-apply variables
    # have no meaning for a destroy but are mandatory, so they get placeholders; so do the
    # required SMTP variables when there is no var file. The location and environment are passed
    # only when given.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $TerraformDir,
        [Parameter(Mandatory = $true)] [string] $SubscriptionId,
        [string] $Location,
        [string] $Environment,
        [string] $VarFile,
        [switch] $IncludeNeon
    )
    $arguments = @("-chdir=$TerraformDir", 'destroy', '-input=false', '-auto-approve')
    if ($VarFile) {
        $arguments += "-var-file=$VarFile"
    }
    $arguments += @(
        "-var=subscription_id=$SubscriptionId",
        '-var=api_image=docker.io/library/unused:teardown',
        '-var=web_image=docker.io/library/unused:teardown',
        '-var=keycloak_image=docker.io/library/unused:teardown',
        '-var=revision_suffix=teardown'
    )
    if ($Location) {
        $arguments += "-var=location=$Location"
    }
    if ($Environment) {
        $arguments += "-var=environment=$Environment"
    }
    if (-not $VarFile) {
        $arguments += '-var=smtp_host=unused.invalid', '-var=smtp_from_address=unused@unused.invalid'
    }
    if (-not $IncludeNeon) {
        $arguments += '-target=azurerm_resource_group.main'
    }
    $arguments
}

function Read-StateOutput {
    # The outputs of a raw Terraform state document (read straight from the state blob, so no
    # `terraform init` is needed and -WhatIf stays read-only) as name -> value.
    [CmdletBinding()]
    param([AllowEmptyString()] [AllowNull()] [string] $StateJson)
    $result = @{}
    if ([string]::IsNullOrWhiteSpace($StateJson)) { return $result }
    $state = $StateJson | ConvertFrom-Json
    if ($state.outputs) {
        foreach ($property in $state.outputs.PSObject.Properties) {
            $result[$property.Name] = $property.Value.value
        }
    }
    $result
}

function Resolve-NeonProjectTarget {
    # Which Neon project -IncludeNeon may delete. The id recorded in the Terraform state always
    # wins; only without it may a project be chosen by name, and then only when exactly one has
    # that exact name - Neon names are not unique, so several matches are never guessed between.
    [CmdletBinding()]
    param(
        [AllowNull()] [AllowEmptyString()] [string] $StateProjectId,
        [AllowEmptyCollection()] [object[]] $NameMatches = @()
    )
    if ($StateProjectId) {
        return [pscustomobject]@{ Action = 'DeleteById'; ProjectId = $StateProjectId; Source = 'state' }
    }
    $candidates = @($NameMatches)
    switch ($candidates.Count) {
        0 { return [pscustomobject]@{ Action = 'None'; ProjectId = $null; Source = $null } }
        1 { return [pscustomobject]@{ Action = 'DeleteById'; ProjectId = $candidates[0].id; Source = 'name' } }
        default { return [pscustomobject]@{ Action = 'Refuse'; ProjectId = $null; Source = 'name' } }
    }
}

function Resolve-KeyVaultPurgeStep {
    # Next step of the Key Vault purge loop (T142). Key Vault names are globally unique and a deleted
    # vault stays soft-deleted (blocking a redeploy under the same name) until purged, so the vault
    # must end up purged, whichever way it was deleted:
    #   - soft-deleted          -> 'Purge' (also one left behind by an earlier run);
    #   - still active          -> 'Wait' (its resource group is still being deleted);
    #   - absent from both lists -> if it existed in this run, 'Wait' until it has been absent for
    #     ConfirmAbsentAttempts consecutive polls (AbsentPolls, this one included: the soft-deleted
    #     listing can lag the deletion), then 'Done'
    #     (already purged, e.g. by terraform destroy); if it never existed, 'Done' right away.
    # After MaxAttempts polls, anything but a purge or a confirmed absence is 'TimedOut'.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [int] $Attempt,
        [Parameter(Mandatory = $true)] [int] $MaxAttempts,
        [Parameter(Mandatory = $true)] [int] $ConfirmAbsentAttempts,
        [int] $AbsentPolls = 0,
        [switch] $Active,
        [switch] $SoftDeleted,
        [switch] $WasPresent
    )
    if ($SoftDeleted) { return 'Purge' }
    if (-not $Active) {
        if (-not $WasPresent -or $AbsentPolls -ge $ConfirmAbsentAttempts) { return 'Done' }
    }
    if ($Attempt -ge $MaxAttempts) { return 'TimedOut' }
    'Wait'
}

Export-ModuleMember -Function Test-TeardownConfirmation, Select-NeonProjectToDelete, Get-DestroyArgument,
    Read-StateOutput, Resolve-NeonProjectTarget, Resolve-KeyVaultPurgeStep
