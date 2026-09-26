# Pester 5+ tests for infra/DeployImages.psm1 — the pure image-resolution and rollout-decision
# logic behind infra/deploy.ps1 (T134, ADR-0018). Run: Invoke-Pester infra/tests
# (Windows PowerShell 5.1 ships Pester 3.4: Install-Module Pester -MinimumVersion 5.0 -Scope CurrentUser)

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../DeployImages.psm1') -Force
}

Describe 'Resolve-ImageReference' {
    It 'resolves a release version to the -release repository' {
        $image = Resolve-ImageReference -Namespace 'acme' -Component api -Version '0.3.0'

        $image.Component | Should -Be 'api'
        $image.Repository | Should -Be 'birrapoint-api-release'
        $image.Tag | Should -Be '0.3.0'
        $image.Reference | Should -Be 'docker.io/acme/birrapoint-api-release:0.3.0'
        $image.Pinned | Should -BeTrue
    }

    It 'resolves an omitted version to latest in the integration repository' {
        $image = Resolve-ImageReference -Namespace 'acme' -Component web

        $image.Repository | Should -Be 'birrapoint-web'
        $image.Tag | Should -Be 'latest'
        $image.Reference | Should -Be 'docker.io/acme/birrapoint-web:latest'
        $image.Pinned | Should -BeFalse
    }

    It 'treats an empty version like an omitted one' {
        (Resolve-ImageReference -Namespace 'acme' -Component keycloak -Version '').Reference |
            Should -Be 'docker.io/acme/birrapoint-keycloak:latest'
    }

    It 'rejects a version that is not X.Y.Z (<version>)' -ForEach @(
        @{ version = 'v1.0.0' }, @{ version = '1.0' }, @{ version = 'latest' }, @{ version = '1.0.0-rc1' }, @{ version = '01.2.3' }
    ) {
        { Resolve-ImageReference -Namespace 'acme' -Component api -Version $version } |
            Should -Throw '*X.Y.Z*'
    }

    It 'rejects an unknown component' {
        { Resolve-ImageReference -Namespace 'acme' -Component db } | Should -Throw
    }

    It 'requires a namespace' {
        { Resolve-ImageReference -Namespace '' -Component api } | Should -Throw
    }
}

Describe 'Get-ContainerAppName' {
    It 'maps <component> to <expected> (same names as infra/terraform/main.tf)' -ForEach @(
        @{ component = 'api'; expected = 'birrapoint-api' },
        @{ component = 'web'; expected = 'birrapoint-web' },
        @{ component = 'keycloak'; expected = 'birrapoint-kc' }
    ) {
        Get-ContainerAppName -NamePrefix 'birrapoint' -Component $component | Should -Be $expected
    }
}

Describe 'Test-DockerHubTag' {
    BeforeEach {
        $env:DOCKERHUB_USERNAME = $null
        $env:DOCKERHUB_TOKEN = $null
    }
    AfterAll {
        $env:DOCKERHUB_USERNAME = $null
        $env:DOCKERHUB_TOKEN = $null
    }

    It 'queries the Docker Hub tag endpoint anonymously without credentials and returns true on 200' {
        Mock Get-HttpStatusCode -ModuleName DeployImages { 200 }
        Mock Invoke-RestMethod -ModuleName DeployImages { throw 'must not log in' }

        Test-DockerHubTag -Namespace 'acme' -Repository 'birrapoint-api-release' -Tag '0.3.0' | Should -BeTrue

        Should -Invoke Get-HttpStatusCode -ModuleName DeployImages -Times 1 -ParameterFilter {
            $Uri -eq 'https://hub.docker.com/v2/namespaces/acme/repositories/birrapoint-api-release/tags/0.3.0' -and
            $Headers.Count -eq 0
        }
    }

    It 'authenticates with DOCKERHUB_USERNAME/DOCKERHUB_TOKEN when set (private repositories)' {
        $env:DOCKERHUB_USERNAME = 'bot'
        $env:DOCKERHUB_TOKEN = 'pat-value'
        Mock Invoke-RestMethod -ModuleName DeployImages { [pscustomobject]@{ access_token = 'jwt-value' } }
        Mock Get-HttpStatusCode -ModuleName DeployImages { 200 }

        Test-DockerHubTag -Namespace 'acme' -Repository 'birrapoint-api' -Tag 'latest' | Should -BeTrue

        Should -Invoke Invoke-RestMethod -ModuleName DeployImages -Times 1 -ParameterFilter {
            $Uri -eq 'https://hub.docker.com/v2/auth/token' -and $Method -eq 'Post' -and
            ($Body | ConvertFrom-Json).identifier -eq 'bot' -and ($Body | ConvertFrom-Json).secret -eq 'pat-value'
        }
        Should -Invoke Get-HttpStatusCode -ModuleName DeployImages -Times 1 -ParameterFilter {
            $Headers['Authorization'] -eq 'Bearer jwt-value'
        }
    }

    It 'returns false on 404' {
        Mock Get-HttpStatusCode -ModuleName DeployImages { 404 }

        Test-DockerHubTag -Namespace 'acme' -Repository 'birrapoint-api' -Tag 'latest' | Should -BeFalse
    }

    It 'throws on any other status so an outage is not mistaken for a missing image' {
        Mock Get-HttpStatusCode -ModuleName DeployImages { 503 }

        { Test-DockerHubTag -Namespace 'acme' -Repository 'birrapoint-api' -Tag 'latest' } | Should -Throw '*503*'
    }
}

Describe 'New-RevisionSuffix' {
    BeforeAll {
        $at = [datetime]::new(2026, 9, 26, 10, 30, 5)
    }

    It 'encodes a release version' {
        New-RevisionSuffix -Tag '0.3.0' -AppName 'birrapoint-api' -Timestamp $at | Should -Be 'v0-3-0-20260926103005'
    }

    It 'encodes latest' {
        New-RevisionSuffix -Tag 'latest' -AppName 'birrapoint-api' -Timestamp $at | Should -Be 'latest-20260926103005'
    }

    It 'encodes an infrastructure apply label' {
        New-RevisionSuffix -Tag 'infra' -AppName 'birrapoint-api' -Timestamp $at | Should -Be 'infra-20260926103005'
    }

    It 'produces a valid Container Apps revision suffix (no "--", starts with a letter)' {
        $suffix = New-RevisionSuffix -Tag '10.20.30' -AppName 'birrapoint-api' -Timestamp $at
        $suffix | Should -Match '^[a-z][a-z0-9-]*[a-z0-9]$'
        $suffix | Should -Not -Match '--'
    }

    It 'rejects a revision name (app name, "--", suffix) longer than 64 characters' {
        { New-RevisionSuffix -Tag '0.3.0' -AppName ('a' * 45) -Timestamp $at } | Should -Throw '*64*'
    }
}

Describe 'Test-RolloutNeeded' {
    BeforeAll {
        $pinned = Resolve-ImageReference -Namespace 'acme' -Component api -Version '0.3.0'
        $latest = Resolve-ImageReference -Namespace 'acme' -Component api
    }

    It 'skips a pinned version the ready revision already runs' {
        Test-RolloutNeeded -Image $pinned -ReadyImage 'docker.io/acme/birrapoint-api-release:0.3.0' -LatestIsReady |
            Should -BeFalse
    }

    It 'rolls out a pinned version the ready revision does not run' {
        Test-RolloutNeeded -Image $pinned -ReadyImage 'docker.io/acme/birrapoint-api-release:0.2.0' -LatestIsReady |
            Should -BeTrue
    }

    It 'rolls out when the latest revision is not the ready one, even if the ready one matches (failed earlier rollout)' {
        Test-RolloutNeeded -Image $pinned -ReadyImage 'docker.io/acme/birrapoint-api-release:0.3.0' |
            Should -BeTrue
    }

    It 'always rolls out latest, whose content may have moved under the same reference' {
        Test-RolloutNeeded -Image $latest -ReadyImage 'docker.io/acme/birrapoint-api:latest' -LatestIsReady |
            Should -BeTrue
    }

    It 'skips latest right after an infrastructure apply created a fresh revision that pulled it' {
        Test-RolloutNeeded -Image $latest -ReadyImage 'docker.io/acme/birrapoint-api:latest' -LatestIsReady -JustRefreshed |
            Should -BeFalse
    }

    It 'rolls out when there is no ready revision at all' {
        Test-RolloutNeeded -Image $pinned -ReadyImage $null | Should -BeTrue
    }
}

Describe 'Get-RevisionOutcome' {
    It 'is Succeeded when provisioned and healthy' {
        Get-RevisionOutcome -ProvisioningState 'Provisioned' -RunningState 'Running' -HealthState 'Healthy' -Replicas 1 -MinReplicas 1 |
            Should -Be 'Succeeded'
    }

    It 'is Succeeded for a provisioned revision scaled to zero in an app allowed to scale to zero' {
        Get-RevisionOutcome -ProvisioningState 'Provisioned' -RunningState 'Stopped' -HealthState 'None' -Replicas 0 -MinReplicas 0 |
            Should -Be 'Succeeded'
    }

    It 'is Pending with zero replicas when the app must keep replicas running' {
        Get-RevisionOutcome -ProvisioningState 'Provisioned' -RunningState 'Processing' -HealthState 'None' -Replicas 0 -MinReplicas 1 |
            Should -Be 'Pending'
    }

    It 'is Pending while provisioning' {
        Get-RevisionOutcome -ProvisioningState 'Provisioning' -RunningState 'Processing' -HealthState 'None' -Replicas 0 -MinReplicas 1 |
            Should -Be 'Pending'
    }

    It 'is Failed when <field> is <state>' -ForEach @(
        @{ field = 'provisioning'; state = 'Failed'; p = 'Failed'; r = 'Processing' },
        @{ field = 'provisioning'; state = 'Deprovisioned'; p = 'Deprovisioned'; r = 'Stopped' },
        @{ field = 'provisioning'; state = 'Deprovisioning'; p = 'Deprovisioning'; r = 'Stopped' },
        @{ field = 'running'; state = 'Failed'; p = 'Provisioned'; r = 'Failed' },
        @{ field = 'running'; state = 'Degraded'; p = 'Provisioned'; r = 'Degraded' }
    ) {
        Get-RevisionOutcome -ProvisioningState $p -RunningState $r -HealthState 'Unhealthy' -Replicas 1 -MinReplicas 1 |
            Should -Be 'Failed'
    }

    It 'is Pending while provisioned but not yet healthy' {
        Get-RevisionOutcome -ProvisioningState 'Provisioned' -RunningState 'Activating' -HealthState 'Unhealthy' -Replicas 1 -MinReplicas 1 |
            Should -Be 'Pending'
    }
}
