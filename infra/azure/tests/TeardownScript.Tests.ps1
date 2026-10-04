# Pester 5+ orchestration tests for infra/azure/teardown.ps1: the script runs end to end against stub
# `az`, `terraform` and Invoke-RestMethod, so what it calls (and does not call) is asserted without
# credentials. The stubs are global functions (they override the executables and the cmdlet for the
# script run), which works on Windows PowerShell 5.1 and PowerShell 7.
# Run: Invoke-Pester infra/azure/tests

BeforeAll {
    $script:Repo = Join-Path $TestDrive 'repo'
    New-Item -ItemType Directory -Path (Join-Path $script:Repo 'terraform') -Force | Out-Null
    # A copy, so the script's removal of terraform/.terraform never touches the real working tree.
    Copy-Item (Join-Path $PSScriptRoot '../teardown.ps1') $script:Repo
    Copy-Item (Join-Path $PSScriptRoot '../Teardown.psm1') $script:Repo

    $global:Td = $null
    $global:TdCalls = New-Object System.Collections.ArrayList

    function global:az {
        $global:LASTEXITCODE = 0
        $line = $args -join ' '
        [void] $global:TdCalls.Add("az $line")
        if ($line -like 'account show*') { return '{"name":"test-subscription","id":"00000000-0000-0000-0000-000000000000"}' }
        if ($line -like 'group exists*') { if ($global:Td.GroupDeleted) { return 'false' } else { return 'true' } }
        if ($line -like 'group delete*') { $global:Td.GroupDeleted = $true; return }
        if ($line -like 'monitor log-analytics workspace list*') { return 'birrapoint-prod-law' }
        if ($line -like 'keyvault list-deleted*') { if ($global:Td.VaultPurged) { return } else { return 'birrapoint-prod-kv' } }
        if ($line -like 'keyvault purge*') { $global:Td.VaultPurged = $true; return }
    }

    function global:terraform {
        $global:LASTEXITCODE = 0
        $sub = $args[1]
        [void] $global:TdCalls.Add('terraform ' + (($args | Select-Object -Skip 1) -join ' '))
        switch ($sub) {
            'state' { $global:Td.StateList }
            'output' {
                $name = $args[3]
                if ($name -eq 'environment') { $global:Td.StateEnvironment }
                elseif ($name -eq 'neon_project_id' -and $global:Td.StateNeonId) { $global:Td.StateNeonId }
                else { $global:LASTEXITCODE = 1 }
            }
            'destroy' { $global:LASTEXITCODE = $global:Td.DestroyExit }
        }
    }

    function global:Invoke-RestMethod {
        param($Uri, $Method = 'Get', $Headers, $TimeoutSec)
        if ($Method -eq 'Delete') {
            [void] $global:TdCalls.Add('neon DELETE ' + ($Uri -replace '^.*/projects/', ''))
            $global:Td.NeonDeleted += ($Uri -replace '^.*/projects/', '')
            return
        }
        $live = @($global:Td.NeonProjects | Where-Object { $global:Td.NeonDeleted -notcontains $_.id })
        if ($Uri -like '*/organizations') { return [pscustomobject]@{ organizations = @() } }
        if ($Uri -like '*/projects?*') { return [pscustomobject]@{ projects = $live } }
        $id = $Uri -replace '^.*/projects/', ''
        $found = $live | Where-Object { $_.id -eq $id }
        if (-not $found) {
            $ex = New-Object System.Exception 'Not Found'
            Add-Member -InputObject $ex -MemberType NoteProperty -Name Response -Value ([pscustomobject]@{ StatusCode = 404 })
            throw $ex
        }
        [pscustomobject]@{ project = $found }
    }

    function script:New-Scenario {
        @{
            StateList        = @('azurerm_resource_group.main')
            StateEnvironment = 'prod'
            StateNeonId      = 'p-state'
            DestroyExit      = 0
            GroupDeleted     = $false
            VaultPurged      = $false
            NeonDeleted      = @()
            NeonProjects     = @(
                [pscustomobject]@{ id = 'p-state'; name = 'birrapoint-prod-neon' },
                [pscustomobject]@{ id = 'p-dup'; name = 'birrapoint-prod-neon' },
                [pscustomobject]@{ id = 'p-aws'; name = 'birrapoint-prod-aws-neon' }
            )
        }
    }

    function script:Invoke-Teardown {
        # Runs the script; returns what it threw (or $null) so the calls can be inspected either way.
        param([hashtable] $Scenario, [hashtable] $Parameters = @{})
        $global:Td = $Scenario
        $global:TdCalls.Clear()
        $saved = @{}
        foreach ($name in 'NEON_API_KEY', 'TF_VAR_neon_org_id', 'TF_WORKSPACE') { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
        $env:NEON_API_KEY = 'test-key'; $env:TF_VAR_neon_org_id = 'org-1'; $env:TF_WORKSPACE = 'ws'
        $thrown = $null
        try { & (Join-Path $script:Repo 'teardown.ps1') -Environment prod @Parameters *>&1 | Out-Null }
        catch { $thrown = $_.Exception.Message }
        finally { foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) } }
        $thrown
    }

    function script:Get-Mutation {
        @($global:TdCalls | Where-Object {
                $_ -match '^az group delete' -or $_ -match '^az keyvault purge' -or
                $_ -match '^az monitor log-analytics workspace delete' -or
                $_ -match '^terraform destroy' -or $_ -match '^neon DELETE'
            })
    }
}

AfterAll {
    foreach ($name in 'az', 'terraform', 'Invoke-RestMethod') {
        Remove-Item "Function:\global:$name" -ErrorAction SilentlyContinue
    }
    Remove-Variable Td, TdCalls -Scope Global -ErrorAction SilentlyContinue
}

Describe 'teardown.ps1 orchestration' {
    It '-WhatIf issues no mutating call (no az delete/purge, no destroy, no Neon DELETE)' {
        $thrown = Invoke-Teardown (New-Scenario) @{ WhatIf = $true }

        $thrown | Should -BeNullOrEmpty
        Get-Mutation | Should -BeNullOrEmpty
        ($global:TdCalls -join "`n") | Should -Match 'az group exists'
    }

    It 'a failed terraform destroy still sweeps what it left behind' {
        $scenario = New-Scenario
        $scenario.DestroyExit = 1

        $null = Invoke-Teardown $scenario @{ Force = $true }

        $calls = $global:TdCalls -join "`n"
        $calls | Should -Match 'terraform destroy'
        $calls | Should -Match 'az group delete'
        $calls | Should -Match 'az keyvault purge'
        $calls | Should -Match 'neon DELETE p-state'
    }

    It 'aborts before any mutation when the workspace holds another environment' {
        $scenario = New-Scenario
        $scenario.StateEnvironment = 'staging'

        $thrown = Invoke-Teardown $scenario @{ Force = $true }

        $thrown | Should -Match "holds environment 'staging'"
        Get-Mutation | Should -BeNullOrEmpty
    }

    It 'deletes only the Neon project id of the state, never another project with the same name' {
        $thrown = Invoke-Teardown (New-Scenario) @{ Force = $true }

        $thrown | Should -BeNullOrEmpty
        @($global:TdCalls | Where-Object { $_ -like 'neon DELETE*' }) | Should -Be @('neon DELETE p-state')
    }

    It 'refuses, before any mutation, when the state project id carries the other cloud name' {
        $scenario = New-Scenario
        $scenario.StateNeonId = 'p-aws'

        $thrown = Invoke-Teardown $scenario @{ Force = $true }

        $thrown | Should -Match 'p-aws'
        Get-Mutation | Should -BeNullOrEmpty
    }

    It 'with an empty state never deletes by name: it only reports the match' {
        $scenario = New-Scenario
        $scenario.StateList = @()
        $scenario.StateNeonId = $null

        $null = Invoke-Teardown $scenario @{ Force = $true }

        @($global:TdCalls | Where-Object { $_ -like 'neon DELETE*' }) | Should -BeNullOrEmpty
    }

    It 'with an empty state deletes an explicit -NeonProjectId whose name matches' {
        $scenario = New-Scenario
        $scenario.StateList = @()
        $scenario.StateNeonId = $null

        $thrown = Invoke-Teardown $scenario @{ Force = $true; NeonProjectId = 'p-dup' }

        $thrown | Should -BeNullOrEmpty
        @($global:TdCalls | Where-Object { $_ -like 'neon DELETE*' }) | Should -Be @('neon DELETE p-dup')
    }

    It 'refuses an explicit -NeonProjectId named for the other cloud, before any mutation' {
        $scenario = New-Scenario
        $scenario.StateList = @()
        $scenario.StateNeonId = $null

        $thrown = Invoke-Teardown $scenario @{ Force = $true; NeonProjectId = 'p-aws' }

        $thrown | Should -Match 'p-aws'
        Get-Mutation | Should -BeNullOrEmpty
    }
}
