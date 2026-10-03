# Pester 5+ tests for infra/Teardown.psm1 - the pure decisions behind infra/teardown.ps1 (T140, T143).
# Run: Invoke-Pester infra/tests
# (Windows PowerShell 5.1 ships Pester 3.4: Install-Module Pester -MinimumVersion 5.0 -Scope CurrentUser)

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../Teardown.psm1') -Force
}

Describe 'Test-TeardownConfirmation' {
    It 'accepts the exact expected word' {
        Test-TeardownConfirmation -Expected 'birrapoint' -Answer 'birrapoint' | Should -BeTrue
    }

    It 'tolerates surrounding whitespace' {
        Test-TeardownConfirmation -Expected 'birrapoint' -Answer '  birrapoint ' | Should -BeTrue
    }

    It 'rejects <answer>' -ForEach @(
        @{ answer = '' }, @{ answer = 'y' }, @{ answer = 'yes' }, @{ answer = 'BirraPoint' }, @{ answer = 'birrapoint2' }
    ) {
        Test-TeardownConfirmation -Expected 'birrapoint' -Answer $answer | Should -BeFalse
    }

    It 'rejects a null answer (non-interactive prompt)' {
        Test-TeardownConfirmation -Expected 'birrapoint' -Answer $null | Should -BeFalse
    }
}

Describe 'Select-NeonProjectToDelete' {
    BeforeAll {
        $projects = @(
            [pscustomobject]@{ id = 'p-1'; name = 'birrapoint' },
            [pscustomobject]@{ id = 'p-2'; name = 'birrapoint-old' },
            [pscustomobject]@{ id = 'p-3'; name = 'mybirrapoint' },
            [pscustomobject]@{ id = 'p-4'; name = 'other' }
        )
    }

    It 'selects only exact name matches' {
        $selected = @(Select-NeonProjectToDelete -Projects $projects -Name 'birrapoint')
        $selected.Count | Should -Be 1
        $selected[0].id | Should -Be 'p-1'
    }

    It 'selects nothing when no project has the exact name' {
        @(Select-NeonProjectToDelete -Projects $projects -Name 'birra').Count | Should -Be 0
    }

    It 'handles an empty project list' {
        @(Select-NeonProjectToDelete -Projects @() -Name 'birrapoint').Count | Should -Be 0
    }
}

Describe 'ConvertTo-EnvironmentName' {
    It 'lower-cases a valid environment' {
        ConvertTo-EnvironmentName 'PROD' | Should -Be 'prod'
    }

    It 'rejects <value>' -ForEach @(
        @{ value = '' }, @{ value = 'a' }, @{ value = '1prod' }, @{ value = 'prod-eu' }, @{ value = 'abcdefghijk' }
    ) {
        { ConvertTo-EnvironmentName $value } | Should -Throw
    }
}

Describe 'Get-TeardownResourceName' {
    It 'follows birrapoint-<environment>-<acronym> (ADR-0021)' {
        Get-TeardownResourceName -Environment 'PROD' -Resource ResourceGroup | Should -Be 'birrapoint-prod-rg'
        Get-TeardownResourceName -Environment 'Dev' -Resource KeyVault | Should -Be 'birrapoint-dev-kv'
        Get-TeardownResourceName -Environment 'prod' -Resource NeonProject | Should -Be 'birrapoint-prod-neon'
    }

    It 'rejects an invalid environment' {
        { Get-TeardownResourceName -Environment 'x' -Resource ResourceGroup } | Should -Throw
    }
}

Describe 'Get-DestroyArgument' {
    BeforeAll {
        $common = @{ TerraformDir = 'C:/repo/infra/terraform'; Environment = 'prod' }
    }

    It 'always destroys everything: never a -target' {
        $arguments = Get-DestroyArgument @common -VarFile 'C:/repo/infra/terraform/terraform.tfvars'

        ($arguments | Where-Object { $_ -like '-target*' }) | Should -BeNullOrEmpty
        $arguments[0..1] | Should -Be @('-chdir=C:/repo/infra/terraform', 'destroy')
        $arguments | Should -Contain '-input=false'
        $arguments | Should -Contain '-auto-approve'
    }

    It 'passes the environment so the destroy resolves the same resource names' {
        Get-DestroyArgument @common -VarFile 'x.tfvars' | Should -Contain '-var=environment=prod'
    }

    It 'does not pass the subscription (ARM_SUBSCRIPTION_ID or the az default apply)' {
        (Get-DestroyArgument @common -VarFile 'x.tfvars' | Where-Object { $_ -like '*subscription_id*' }) | Should -BeNullOrEmpty
    }

    It 'passes the location only when given' {
        (Get-DestroyArgument @common -VarFile 'x.tfvars' | Where-Object { $_ -like '-var=location=*' }) | Should -BeNullOrEmpty
        Get-DestroyArgument @common -VarFile 'x.tfvars' -Location 'northeurope' | Should -Contain '-var=location=northeurope'
    }

    It 'puts the var file before the -var flags' {
        $arguments = @(Get-DestroyArgument @common -VarFile 'x.tfvars')
        $varFileIndex = [array]::IndexOf($arguments, '-var-file=x.tfvars')
        $firstVarIndex = [array]::IndexOf($arguments, '-var=environment=prod')

        $varFileIndex | Should -BeGreaterThan -1
        $varFileIndex | Should -BeLessThan $firstVarIndex
    }

    It 'passes a placeholder for the mandatory image namespace and none of the removed image variables' {
        $arguments = Get-DestroyArgument @common -VarFile 'x.tfvars'

        ($arguments | Where-Object { $_ -like '-var=image_namespace=*' }).Count | Should -Be 1
        foreach ($name in 'api_image', 'web_image', 'keycloak_image', 'revision_suffix') {
            ($arguments | Where-Object { $_ -like "-var=$name=*" }) | Should -BeNullOrEmpty
        }
    }

    It 'passes a placeholder for the mandatory release version, which has no default' {
        $arguments = Get-DestroyArgument @common -VarFile 'x.tfvars'

        ($arguments | Where-Object { $_ -eq '-var=release_version=latest' }).Count | Should -Be 1
    }

    It 'uses the var file when there is one' {
        $arguments = Get-DestroyArgument @common -VarFile 'C:/repo/infra/terraform/terraform.tfvars'

        $arguments | Should -Contain '-var-file=C:/repo/infra/terraform/terraform.tfvars'
        ($arguments | Where-Object { $_ -like '-var=smtp_*' }) | Should -BeNullOrEmpty
    }

    It 'supplies placeholders for the required SMTP variables when there is no var file' {
        $arguments = Get-DestroyArgument @common

        ($arguments | Where-Object { $_ -like '-var-file=*' }) | Should -BeNullOrEmpty
        ($arguments | Where-Object { $_ -like '-var=smtp_host=*' }).Count | Should -Be 1
        ($arguments | Where-Object { $_ -like '-var=smtp_from_address=*' }).Count | Should -Be 1
    }
}

Describe 'Resolve-NeonProjectTarget' {
    It 'deletes the single exact-name match' {
        $found = @([pscustomobject]@{ id = 'p-1'; name = 'birrapoint-prod-neon' })
        $target = Resolve-NeonProjectTarget -NameMatches $found
        $target.Action | Should -Be 'DeleteById'
        $target.ProjectId | Should -Be 'p-1'
    }

    It 'refuses to guess between several projects with the same name' {
        $found = @([pscustomobject]@{ id = 'p-1'; name = 'x' }, [pscustomobject]@{ id = 'p-2'; name = 'x' })
        $target = Resolve-NeonProjectTarget -NameMatches $found
        $target.Action | Should -Be 'Refuse'
        $target.ProjectId | Should -BeNullOrEmpty
    }

    It 'has nothing to do when no project has the name' {
        (Resolve-NeonProjectTarget -NameMatches @()).Action | Should -Be 'None'
    }
}

Describe 'Test-StateEnvironment' {
    It 'accepts an empty state (nothing deployed yet)' -ForEach @(
        @{ state = $null }, @{ state = '' }, @{ state = '  ' }
    ) {
        Test-StateEnvironment -Expected 'PROD' -StateEnvironment $state | Should -BeTrue
    }

    It 'accepts the same environment regardless of casing' {
        Test-StateEnvironment -Expected 'PROD' -StateEnvironment 'prod' | Should -BeTrue
        Test-StateEnvironment -Expected 'prod' -StateEnvironment ' PROD ' | Should -BeTrue
    }

    It 'rejects a workspace that holds another environment' {
        Test-StateEnvironment -Expected 'dev' -StateEnvironment 'prod' | Should -BeFalse
    }

    It 'does not accept a prefix of the environment' {
        Test-StateEnvironment -Expected 'prod' -StateEnvironment 'prod2' | Should -BeFalse
    }
}

Describe 'Get-EnvironmentVarFile' {
    It 'is environments/ENV.tfvars (lower-cased) under the Terraform directory' {
        $file = Get-EnvironmentVarFile -TerraformDir 'C:/repo/infra/terraform' -Environment 'PROD'
        $file | Should -Be (Join-Path 'C:/repo/infra/terraform' 'environments/prod.tfvars')
    }
}
