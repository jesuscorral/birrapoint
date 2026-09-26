# Pure helpers behind infra/teardown.ps1 (T140): the confirmation check, which Neon projects may
# be deleted, and the `terraform destroy` arguments. Kept free of Azure/Neon calls so they are
# unit-testable (infra/tests/Teardown.Tests.ps1). Windows PowerShell 5.1 and PowerShell 7.

$ErrorActionPreference = 'Stop'

function Test-TeardownConfirmation {
    # The operator must type the exact expected word (the name prefix); anything else - including
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
    # Only projects whose name is exactly the deployment's name prefix; never a prefix or
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
    # it every Container App, the environment and Log Analytics - is targeted: the Neon project
    # and the generated passwords stay in the state, so the next deploy reconnects to the same
    # database with matching secrets. -IncludeNeon destroys everything.
    # The per-apply variables have no meaning for a destroy but are mandatory, so they get
    # placeholders; so do the required SMTP variables when there is no var file.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $TerraformDir,
        [Parameter(Mandatory = $true)] [string] $SubscriptionId,
        [Parameter(Mandatory = $true)] [string] $Location,
        [string] $VarFile,
        [switch] $IncludeNeon
    )
    $arguments = @(
        "-chdir=$TerraformDir", 'destroy', '-input=false', '-auto-approve',
        "-var=subscription_id=$SubscriptionId",
        "-var=location=$Location",
        '-var=api_image=docker.io/library/unused:teardown',
        '-var=web_image=docker.io/library/unused:teardown',
        '-var=keycloak_image=docker.io/library/unused:teardown',
        '-var=revision_suffix=teardown'
    )
    if ($VarFile) {
        $arguments += "-var-file=$VarFile"
    }
    else {
        $arguments += '-var=smtp_host=unused.invalid', '-var=smtp_from_address=unused@unused.invalid'
    }
    if (-not $IncludeNeon) {
        $arguments += '-target=azurerm_resource_group.main'
    }
    $arguments
}

Export-ModuleMember -Function Test-TeardownConfirmation, Select-NeonProjectToDelete, Get-DestroyArgument
