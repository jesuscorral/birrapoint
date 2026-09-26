# Pester 5+ tests for infra/Teardown.psm1 - the pure decisions behind infra/teardown.ps1 (T140).
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

Describe 'Get-DestroyArgument' {
    BeforeAll {
        $common = @{ TerraformDir = 'C:/repo/infra/terraform'; SubscriptionId = 'sub-1' }
    }

    It 'targets only the application resource group by default (Neon and its state survive)' {
        $arguments = Get-DestroyArgument @common -VarFile 'C:/repo/infra/terraform/terraform.tfvars'

        $arguments | Should -Contain '-target=azurerm_resource_group.main'
        $arguments[0..1] | Should -Be @('-chdir=C:/repo/infra/terraform', 'destroy')
        $arguments | Should -Contain '-input=false'
        $arguments | Should -Contain '-auto-approve'
    }

    It 'destroys everything with -IncludeNeon (no -target)' {
        $arguments = Get-DestroyArgument @common -VarFile 'x.tfvars' -IncludeNeon

        ($arguments | Where-Object { $_ -like '-target=*' }) | Should -BeNullOrEmpty
    }

    It 'passes the location only when given (tfvars or the variable default apply otherwise)' {
        (Get-DestroyArgument @common -VarFile 'x.tfvars' | Where-Object { $_ -like '-var=location=*' }) | Should -BeNullOrEmpty
        Get-DestroyArgument @common -VarFile 'x.tfvars' -Location 'northeurope' | Should -Contain '-var=location=northeurope'
    }

    It 'puts the var file before the -var flags, so the script values win (same order as deploy.ps1)' {
        $arguments = @(Get-DestroyArgument @common -VarFile 'x.tfvars')
        $varFileIndex = [array]::IndexOf($arguments, '-var-file=x.tfvars')
        $firstVarIndex = [array]::IndexOf($arguments, '-var=subscription_id=sub-1')

        $varFileIndex | Should -BeGreaterThan -1
        $varFileIndex | Should -BeLessThan $firstVarIndex
    }

    It 'passes the subscription and placeholders for the per-apply variables' {
        $arguments = Get-DestroyArgument @common -VarFile 'x.tfvars'

        $arguments | Should -Contain '-var=subscription_id=sub-1'
        foreach ($name in 'api_image', 'web_image', 'keycloak_image') {
            ($arguments | Where-Object { $_ -like "-var=$name=*" }).Count | Should -Be 1
        }
        $arguments | Should -Contain '-var=revision_suffix=teardown'
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

Describe 'Read-StateOutput' {
    It 'returns the outputs of a Terraform state document' {
        $state = @{
            version = 4
            outputs = @{
                resource_group_name = @{ value = 'rg-custom'; type = 'string' }
                neon_project_id     = @{ value = 'young-sun-123'; type = 'string' }
            }
        } | ConvertTo-Json -Depth 5

        $outputs = Read-StateOutput -StateJson $state
        $outputs.resource_group_name | Should -Be 'rg-custom'
        $outputs.neon_project_id | Should -Be 'young-sun-123'
    }

    It 'returns an empty result for a state without outputs (nothing applied yet)' {
        $outputs = Read-StateOutput -StateJson '{"version":4,"outputs":{}}'
        $outputs.resource_group_name | Should -BeNullOrEmpty
    }

    It 'returns an empty result for no state at all' {
        (Read-StateOutput -StateJson '').resource_group_name | Should -BeNullOrEmpty
    }
}

Describe 'Resolve-NeonProjectTarget' {
    It 'uses the project id recorded in the state' {
        $target = Resolve-NeonProjectTarget -StateProjectId 'young-sun-123' -NameMatches @()
        $target.Action | Should -Be 'DeleteById'
        $target.ProjectId | Should -Be 'young-sun-123'
    }

    It 'prefers the state id over name matches' {
        $found = @([pscustomobject]@{ id = 'other-1'; name = 'birrapoint' })
        (Resolve-NeonProjectTarget -StateProjectId 'young-sun-123' -NameMatches $found).ProjectId | Should -Be 'young-sun-123'
    }

    It 'falls back to a single exact-name match when there is no state' {
        $found = @([pscustomobject]@{ id = 'p-1'; name = 'birrapoint' })
        $target = Resolve-NeonProjectTarget -StateProjectId $null -NameMatches $found
        $target.Action | Should -Be 'DeleteById'
        $target.ProjectId | Should -Be 'p-1'
    }

    It 'refuses to guess between several projects with the same name' {
        $found = @([pscustomobject]@{ id = 'p-1'; name = 'birrapoint' }, [pscustomobject]@{ id = 'p-2'; name = 'birrapoint' })
        $target = Resolve-NeonProjectTarget -StateProjectId $null -NameMatches $found
        $target.Action | Should -Be 'Refuse'
        $target.ProjectId | Should -BeNullOrEmpty
    }

    It 'has nothing to do when neither the state nor a name match knows a project' {
        (Resolve-NeonProjectTarget -StateProjectId $null -NameMatches @()).Action | Should -Be 'None'
    }
}
