# Pester 5+ tests for infra/DeployImages.psm1 — the pure image-resolution and rollout-decision
# logic behind infra/deploy.ps1 (T134). Run: Invoke-Pester infra/tests

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
    It 'queries the Docker Hub tag endpoint and returns true on 200' {
        Mock Get-HttpStatusCode -ModuleName DeployImages { 200 }

        Test-DockerHubTag -Namespace 'acme' -Repository 'birrapoint-api-release' -Tag '0.3.0' | Should -BeTrue

        Should -Invoke Get-HttpStatusCode -ModuleName DeployImages -Times 1 -ParameterFilter {
            $Uri -eq 'https://hub.docker.com/v2/namespaces/acme/repositories/birrapoint-api-release/tags/0.3.0'
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
        New-RevisionSuffix -Tag '0.3.0' -Timestamp $at | Should -Be 'v0-3-0-20260926103005'
    }

    It 'encodes latest' {
        New-RevisionSuffix -Tag 'latest' -Timestamp $at | Should -Be 'latest-20260926103005'
    }

    It 'produces a valid Container Apps revision suffix' {
        New-RevisionSuffix -Tag '10.20.30' -Timestamp $at | Should -Match '^[a-z][a-z0-9-]*[a-z0-9]$'
    }
}

Describe 'Test-RolloutNeeded' {
    It 'skips a pinned version the app already runs' {
        $image = Resolve-ImageReference -Namespace 'acme' -Component api -Version '0.3.0'

        Test-RolloutNeeded -Image $image -CurrentReference 'docker.io/acme/birrapoint-api-release:0.3.0' | Should -BeFalse
    }

    It 'rolls out a pinned version the app does not run yet' {
        $image = Resolve-ImageReference -Namespace 'acme' -Component api -Version '0.3.0'

        Test-RolloutNeeded -Image $image -CurrentReference 'docker.io/acme/birrapoint-api-release:0.2.0' | Should -BeTrue
    }

    It 'always rolls out latest, whose content may have moved under the same reference' {
        $image = Resolve-ImageReference -Namespace 'acme' -Component api

        Test-RolloutNeeded -Image $image -CurrentReference 'docker.io/acme/birrapoint-api:latest' | Should -BeTrue
    }
}
