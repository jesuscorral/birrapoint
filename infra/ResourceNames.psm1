# Resource naming convention of the cloud deployment (T142, ADR-0021): every deployed resource is
# named birrapoint-<environment>-<resource acronym>, with the environment lower-cased (Container
# Apps only allow lowercase). Must match the locals in infra/terraform/main.tf. Shared by
# infra/deploy.ps1 and infra/teardown.ps1; pure, so unit-testable (infra/tests/ResourceNames.Tests.ps1).
# Compatible with Windows PowerShell 5.1 and PowerShell 7.

$ErrorActionPreference = 'Stop'

$script:ResourceAcronyms = @{
    ResourceGroup           = 'rg'
    LogAnalyticsWorkspace   = 'log'
    ContainerAppEnvironment = 'cae'
    KeyVault                = 'kv'
    NeonProject             = 'neon'
}

# Component -> Container App acronym.
$script:AppAcronyms = @{ api = 'api'; web = 'web'; keycloak = 'kc' }

function ConvertTo-EnvironmentName {
    # Same rule as Terraform's `environment` variable: 2-10 letters/digits starting with a letter
    # (the Key Vault name birrapoint-<environment>-kv is limited to 24 characters).
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)] [AllowEmptyString()] [string] $Environment)
    if ($Environment -notmatch '^[A-Za-z][A-Za-z0-9]{1,9}$') {
        throw "Invalid environment '$Environment': 2-10 letters or digits, starting with a letter (e.g. PROD)."
    }
    $Environment.ToLowerInvariant()
}

function Get-ResourceName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Environment,
        [Parameter(Mandatory = $true)]
        [ValidateSet('ResourceGroup', 'LogAnalyticsWorkspace', 'ContainerAppEnvironment', 'KeyVault', 'NeonProject')]
        [string] $Resource
    )
    "birrapoint-$(ConvertTo-EnvironmentName $Environment)-$($script:ResourceAcronyms[$Resource])"
}

function Get-ContainerAppName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Environment,
        [Parameter(Mandatory = $true)] [ValidateSet('api', 'web', 'keycloak')] [string] $Component
    )
    "birrapoint-$(ConvertTo-EnvironmentName $Environment)-$($script:AppAcronyms[$Component])"
}

function Get-StateLocation {
    # Where the Terraform state of an environment lives. It is created by deploy.ps1 before
    # Terraform runs and outlives the application resource group (teardown.ps1 keeps it by
    # default). The storage account name must be globally unique and alphanumeric only (3-24):
    # birrapoint<environment>st plus the start of the subscription id, stable per subscription.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Environment,
        [Parameter(Mandatory = $true)] [string] $SubscriptionId
    )
    $name = ConvertTo-EnvironmentName $Environment
    $storage = ("birrapoint$($name)st" + ($SubscriptionId -replace '[^A-Za-z0-9]', '')).ToLowerInvariant()
    if ($storage.Length -gt 24) { $storage = $storage.Substring(0, 24) }
    [pscustomobject]@{
        ResourceGroup  = "birrapoint-$name-tfstate-rg"
        StorageAccount = $storage
        Container      = 'tfstate'
        Key            = "birrapoint-$name.tfstate"
    }
}

Export-ModuleMember -Function ConvertTo-EnvironmentName, Get-ResourceName, Get-ContainerAppName, Get-StateLocation
