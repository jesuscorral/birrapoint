# Pure helpers behind infra/teardown.ps1 (T140, T143): the confirmation check, resource names,
# which Neon project may be deleted, and the `terraform destroy` arguments. Kept free of
# Azure/Neon calls so they are unit-testable (infra/tests/Teardown.Tests.ps1). Windows PowerShell
# 5.1 and PowerShell 7.

$ErrorActionPreference = 'Stop'

# Resource acronyms; must match the locals in infra/terraform/main.tf (ADR-0021).
$script:ResourceAcronyms = @{
    ResourceGroup = 'rg'
    KeyVault      = 'kv'
    NeonProject   = 'neon'
}

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

function ConvertTo-EnvironmentName {
    # Same rule as Terraform's `environment` variable: 2-10 letters/digits starting with a letter
    # (the Key Vault name birrapoint-<environment>-kv is limited to 24 characters), lower-cased.
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)] [AllowEmptyString()] [string] $Environment)
    if ($Environment -notmatch '^[A-Za-z][A-Za-z0-9]{1,9}$') {
        throw "Invalid environment '$Environment': 2-10 letters or digits, starting with a letter (e.g. PROD)."
    }
    $Environment.ToLowerInvariant()
}

function Get-TeardownResourceName {
    # birrapoint-<environment>-<acronym> of the resources the sweep looks for.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Environment,
        [Parameter(Mandatory = $true)] [ValidateSet('ResourceGroup', 'KeyVault', 'NeonProject')] [string] $Resource
    )
    "birrapoint-$(ConvertTo-EnvironmentName $Environment)-$($script:ResourceAcronyms[$Resource])"
}

function Test-StateEnvironment {
    # Guards against destroying the wrong environment: the HCP workspace behind TF_WORKSPACE may
    # hold another environment than the one asked for. An empty state (nothing deployed) is fine.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Expected,
        [AllowNull()] [AllowEmptyString()] [string] $StateEnvironment
    )
    if ([string]::IsNullOrWhiteSpace($StateEnvironment)) { return $true }
    $StateEnvironment.Trim().ToLowerInvariant() -ceq $Expected.Trim().ToLowerInvariant()
}

function Get-EnvironmentVarFile {
    # The committed, non-secret inputs of an environment, shared with deploy.yml (ADR-0022).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $TerraformDir,
        [Parameter(Mandatory = $true)] [string] $Environment
    )
    Join-Path $TerraformDir ('environments/{0}.tfvars' -f (ConvertTo-EnvironmentName $Environment))
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

function Resolve-NeonProjectTarget {
    # Which Neon project the teardown deletes: the one with the exact name. Neon names are not
    # unique, so several matches are refused, never guessed between.
    [CmdletBinding()]
    param([AllowEmptyCollection()] [object[]] $NameMatches = @())
    $candidates = @($NameMatches)
    switch ($candidates.Count) {
        0 { return [pscustomobject]@{ Action = 'None'; ProjectId = $null } }
        1 { return [pscustomobject]@{ Action = 'DeleteById'; ProjectId = $candidates[0].id } }
        default { return [pscustomobject]@{ Action = 'Refuse'; ProjectId = $null } }
    }
}

function Get-DestroyArgument {
    # `terraform destroy` arguments: always a full destroy (no -target). The var file comes first
    # so the -var flags win. The image namespace is mandatory but meaningless for a destroy, so it
    # gets a placeholder, as do the required SMTP variables when there is no var file. The
    # subscription comes from ARM_SUBSCRIPTION_ID / the az login; the location only when given.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $TerraformDir,
        [Parameter(Mandatory = $true)] [string] $Environment,
        [string] $Location,
        [string] $VarFile
    )
    $arguments = @("-chdir=$TerraformDir", 'destroy', '-input=false', '-auto-approve')
    if ($VarFile) {
        $arguments += "-var-file=$VarFile"
    }
    $arguments += @(
        "-var=environment=$Environment",
        '-var=image_namespace=unused',
        # Required with no default (ADR-0022); meaningless for a destroy.
        '-var=release_version=latest'
    )
    if ($Location) {
        $arguments += "-var=location=$Location"
    }
    if (-not $VarFile) {
        $arguments += '-var=smtp_host=unused.invalid', '-var=smtp_from_address=unused@unused.invalid'
    }
    $arguments
}

Export-ModuleMember -Function Test-TeardownConfirmation, ConvertTo-EnvironmentName, Get-TeardownResourceName,
    Select-NeonProjectToDelete, Resolve-NeonProjectTarget, Get-DestroyArgument,
    Test-StateEnvironment, Get-EnvironmentVarFile
