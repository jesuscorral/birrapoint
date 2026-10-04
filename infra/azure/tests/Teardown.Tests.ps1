# Pester 5+ tests for infra/azure/Teardown.psm1 - the pure decisions behind infra/azure/teardown.ps1 (T140, T143).
# Run: Invoke-Pester infra/azure/tests
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
    It 'follows birrapoint-ENVIRONMENT-ACRONYM (ADR-0021)' {
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
        $common = @{ TerraformDir = 'C:/repo/infra/azure/terraform'; Environment = 'prod' }
    }

    It 'always destroys everything: never a -target' {
        $arguments = Get-DestroyArgument @common -VarFile 'C:/repo/infra/azure/terraform/terraform.tfvars'

        ($arguments | Where-Object { $_ -like '-target*' }) | Should -BeNullOrEmpty
        $arguments[0..1] | Should -Be @('-chdir=C:/repo/infra/azure/terraform', 'destroy')
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
        $arguments = Get-DestroyArgument @common -VarFile 'C:/repo/infra/azure/terraform/terraform.tfvars'

        $arguments | Should -Contain '-var-file=C:/repo/infra/azure/terraform/terraform.tfvars'
        ($arguments | Where-Object { $_ -like '-var=smtp_*' }) | Should -BeNullOrEmpty
    }

    It 'supplies placeholders for the required SMTP variables when there is no var file' {
        $arguments = Get-DestroyArgument @common

        ($arguments | Where-Object { $_ -like '-var-file=*' }) | Should -BeNullOrEmpty
        ($arguments | Where-Object { $_ -like '-var=smtp_host=*' }).Count | Should -Be 1
        ($arguments | Where-Object { $_ -like '-var=smtp_from_address=*' }).Count | Should -Be 1
    }
}

Describe 'Resolve-NeonDeletionTarget' {
    BeforeAll {
        $expected = 'birrapoint-prod-neon'
        $other = 'birrapoint-prod-aws-neon'
        $mine = [pscustomobject]@{ id = 'p-mine'; name = $expected }
        $dup = [pscustomobject]@{ id = 'p-dup'; name = $expected }
        $foreign = [pscustomobject]@{ id = 'p-foreign'; name = $other }
    }

    It 'deletes the state project id when its name matches' {
        $target = Resolve-NeonDeletionTarget -StateProjectId 'p-mine' -ProjectsFound @($mine, $dup) -ExpectedName $expected
        $target.Action | Should -Be 'DeleteById'
        $target.ProjectId | Should -Be 'p-mine'
    }

    It 'refuses when the state project id carries another name' {
        $target = Resolve-NeonDeletionTarget -StateProjectId 'p-foreign' -ProjectsFound @($foreign, $mine) -ExpectedName $expected
        $target.Action | Should -Be 'Refuse'
        $target.ProjectId | Should -BeNullOrEmpty
    }

    It 'has nothing to delete when the state project id no longer exists' {
        $target = Resolve-NeonDeletionTarget -StateProjectId 'p-gone' -ProjectsFound @($dup) -ExpectedName $expected
        $target.Action | Should -Be 'None'
    }

    It 'only reports a name match when the state is empty and no id is given' {
        $target = Resolve-NeonDeletionTarget -ProjectsFound @($mine) -ExpectedName $expected
        $target.Action | Should -Be 'ReportOnly'
        $target.ProjectId | Should -BeNullOrEmpty
        @($target.Candidates).id | Should -Be @('p-mine')
    }

    It 'has nothing to do when the state is empty and nothing matches' {
        (Resolve-NeonDeletionTarget -ProjectsFound @($foreign) -ExpectedName $expected).Action | Should -Be 'None'
        (Resolve-NeonDeletionTarget -ProjectsFound @() -ExpectedName $expected).Action | Should -Be 'None'
    }

    It 'deletes an explicit id whose name matches' {
        $target = Resolve-NeonDeletionTarget -ExplicitProjectId 'p-mine' -ProjectsFound @($mine, $dup) -ExpectedName $expected
        $target.Action | Should -Be 'DeleteById'
        $target.ProjectId | Should -Be 'p-mine'
    }

    It 'refuses an explicit id with another name' {
        $target = Resolve-NeonDeletionTarget -ExplicitProjectId 'p-foreign' -ProjectsFound @($foreign, $mine) -ExpectedName $expected
        $target.Action | Should -Be 'Refuse'
    }

    It 'refuses when the explicit id and the state id differ' {
        $target = Resolve-NeonDeletionTarget -StateProjectId 'p-mine' -ExplicitProjectId 'p-dup' -ProjectsFound @($mine, $dup) -ExpectedName $expected
        $target.Action | Should -Be 'Refuse'
    }

    It 'never selects a project named for the other cloud, whatever the id source' {
        foreach ($parameters in @{ StateProjectId = 'p-foreign' }, @{ ExplicitProjectId = 'p-foreign' }) {
            $target = Resolve-NeonDeletionTarget @parameters -ProjectsFound @($foreign) -ExpectedName $expected
            $target.Action | Should -Not -Be 'DeleteById'
            $target.ProjectId | Should -BeNullOrEmpty
        }
        (Resolve-NeonDeletionTarget -ProjectsFound @($foreign) -ExpectedName $expected).Action | Should -Not -Be 'DeleteById'
    }

    It 'matches names case-sensitively and exactly' {
        $near = [pscustomobject]@{ id = 'p-near'; name = $expected.ToUpperInvariant() }
        (Resolve-NeonDeletionTarget -StateProjectId 'p-near' -ProjectsFound @($near) -ExpectedName $expected).Action | Should -Be 'Refuse'
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
        # No drive letter: Join-Path throws DriveNotFound for a drive that does not exist (C: on
        # Linux). Separators differ between platforms and PowerShell versions, so compare with '/'.
        $file = Get-EnvironmentVarFile -TerraformDir 'repo/infra/azure/terraform' -Environment 'PROD'
        ($file -replace '\\', '/') | Should -Be 'repo/infra/azure/terraform/environments/prod.tfvars'
    }
}

Describe 'Resolve-StateEnvironment' {
    It 'aborts when the state cannot be listed' {
        { Resolve-StateEnvironment -StateListExitCode 1 -StateList @() -OutputExitCode 0 -Output 'prod' } | Should -Throw '*reading*state*'
    }

    It 'treats a successful, empty state list as nothing deployed' -ForEach @(
        @{ list = @() }, @{ list = @('') }, @{ list = $null }
    ) {
        Resolve-StateEnvironment -StateListExitCode 0 -StateList $list -OutputExitCode 1 -Output '' | Should -Be ''
    }

    It 'returns the environment output of a non-empty state, trimmed' {
        Resolve-StateEnvironment -StateListExitCode 0 -StateList @('azurerm_resource_group.main') -OutputExitCode 0 -Output " PROD`n" | Should -Be 'PROD'
    }

    It 'aborts when a non-empty state has no readable environment output' {
        { Resolve-StateEnvironment -StateListExitCode 0 -StateList @('a.b') -OutputExitCode 1 -Output '' } | Should -Throw '*environment*'
    }

    It 'aborts when a non-empty state has an empty environment output' {
        { Resolve-StateEnvironment -StateListExitCode 0 -StateList @('a.b') -OutputExitCode 0 -Output '  ' } | Should -Throw '*environment*'
    }
}

Describe 'Resolve-NeonOrgId' {
    It 'prefers the explicit -NeonOrgId' {
        Resolve-NeonOrgId -Explicit 'org-a' -EnvValue 'org-b' -Organizations @([pscustomobject]@{ id = 'org-c' }) | Should -Be 'org-a'
    }

    It 'falls back to TF_VAR_neon_org_id, the variable Terraform uses' {
        Resolve-NeonOrgId -Explicit '' -EnvValue ' org-b ' -Organizations @() | Should -Be 'org-b'
    }

    It 'uses the only organization of the API key when nothing is set' {
        Resolve-NeonOrgId -Organizations @([pscustomobject]@{ id = 'org-c'; name = 'mine' }) | Should -Be 'org-c'
    }

    It 'returns nothing for a key without organizations (personal account)' {
        Resolve-NeonOrgId -Organizations @() | Should -BeNullOrEmpty
    }

    It 'refuses to guess between several organizations' {
        $orgs = @([pscustomobject]@{ id = 'org-1' }, [pscustomobject]@{ id = 'org-2' })
        { Resolve-NeonOrgId -Organizations $orgs } | Should -Throw '*NeonOrgId*'
    }
}
