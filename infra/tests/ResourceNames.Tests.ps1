# Pester 5+ tests for infra/ResourceNames.psm1 - the resource naming convention shared by
# infra/deploy.ps1 and infra/teardown.ps1 (T142, ADR-0021). Run: Invoke-Pester infra/tests
# (Windows PowerShell 5.1 ships Pester 3.4: Install-Module Pester -MinimumVersion 5.0 -Scope CurrentUser)

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../ResourceNames.psm1') -Force
}

Describe 'ConvertTo-EnvironmentName' {
    It 'lower-cases <environment>' -ForEach @(
        @{ environment = 'PROD'; expected = 'prod' },
        @{ environment = 'Dev'; expected = 'dev' },
        @{ environment = 'staging2'; expected = 'staging2' }
    ) {
        ConvertTo-EnvironmentName -Environment $environment | Should -Be $expected
    }

    It 'rejects <environment> (same rule as the Terraform variable)' -ForEach @(
        @{ environment = 'p' },
        @{ environment = '1prod' },
        @{ environment = 'pro-d' },
        @{ environment = 'prod_1' },
        @{ environment = 'averylongenv' },
        @{ environment = '' }
    ) {
        { ConvertTo-EnvironmentName -Environment $environment } | Should -Throw
    }
}

Describe 'Get-ResourceName' {
    It 'names <resource> <expected> (same names as infra/terraform/main.tf)' -ForEach @(
        @{ resource = 'ResourceGroup'; expected = 'birrapoint-prod-rg' },
        @{ resource = 'LogAnalyticsWorkspace'; expected = 'birrapoint-prod-log' },
        @{ resource = 'ContainerAppEnvironment'; expected = 'birrapoint-prod-cae' },
        @{ resource = 'KeyVault'; expected = 'birrapoint-prod-kv' },
        @{ resource = 'NeonProject'; expected = 'birrapoint-prod-neon' }
    ) {
        Get-ResourceName -Environment 'PROD' -Resource $resource | Should -Be $expected
    }

    It 'keeps the longest environment within the Key Vault limit of 24 characters' {
        (Get-ResourceName -Environment 'abcdefghij' -Resource KeyVault).Length | Should -BeLessOrEqual 24
    }
}

Describe 'Get-ContainerAppName' {
    It 'maps <component> to <expected> (same names as infra/terraform/main.tf)' -ForEach @(
        @{ component = 'api'; expected = 'birrapoint-prod-api' },
        @{ component = 'web'; expected = 'birrapoint-prod-web' },
        @{ component = 'keycloak'; expected = 'birrapoint-prod-kc' }
    ) {
        Get-ContainerAppName -Environment 'PROD' -Component $component | Should -Be $expected
    }

    It 'follows the environment' {
        Get-ContainerAppName -Environment 'Dev' -Component api | Should -Be 'birrapoint-dev-api'
    }
}

Describe 'Get-StateLocation' {
    BeforeAll {
        $script:Subscription = '0123abcd-4567-89ef-0123-456789abcdef'
    }

    It 'names the state resource group, container and key after the environment' {
        $state = Get-StateLocation -Environment 'PROD' -SubscriptionId $Subscription
        $state.ResourceGroup | Should -Be 'birrapoint-prod-tfstate-rg'
        $state.Container | Should -Be 'tfstate'
        $state.Key | Should -Be 'birrapoint-prod.tfstate'
    }

    It 'derives a stable, valid, subscription-specific storage account name' {
        $state = Get-StateLocation -Environment 'PROD' -SubscriptionId $Subscription
        $state.StorageAccount | Should -Be 'birrapointprodst0123abcd'
        $state.StorageAccount | Should -MatchExactly '^[a-z0-9]{3,24}$'
    }

    It 'keeps the storage account within 24 characters for the longest environment' {
        $state = Get-StateLocation -Environment 'abcdefghij' -SubscriptionId $Subscription
        $state.StorageAccount.Length | Should -BeLessOrEqual 24
        $state.StorageAccount | Should -BeLike 'birrapointabcdefghijst*'
    }
}
