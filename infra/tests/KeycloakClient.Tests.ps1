# Pester 5+ tests for infra/KeycloakClient.psm1 (T140): keeping the SPA client's URLs in Keycloak in
# step with the web app's current URL. Run: Invoke-Pester infra/tests

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../KeycloakClient.psm1') -Force

    function New-SpaClient([string] $Url) {
        [pscustomobject]@{
            id           = 'uuid-1'
            clientId     = 'birrapoint-spa'
            rootUrl      = $Url
            baseUrl      = $Url
            redirectUris = @("$Url/*")
            webOrigins   = @($Url)
            publicClient = $true
            attributes   = [pscustomobject]@{ 'post.logout.redirect.uris' = "$Url/*"; 'pkce.code.challenge.method' = 'S256' }
        }
    }
}

Describe 'Get-SpaClientUpdate' {
    It 'returns nothing when the client already points at the web URL' {
        $client = New-SpaClient 'https://birrapoint-web.new.northeurope.azurecontainerapps.io'
        Get-SpaClientUpdate -Client $client -WebUrl 'https://birrapoint-web.new.northeurope.azurecontainerapps.io' |
            Should -BeNullOrEmpty
    }

    It 'ignores a trailing slash on the web URL' {
        $client = New-SpaClient 'https://web.example'
        Get-SpaClientUpdate -Client $client -WebUrl 'https://web.example/' | Should -BeNullOrEmpty
    }

    It 'rewrites every URL field after the environment got a new domain' {
        $client = New-SpaClient 'https://birrapoint-web.old.northeurope.azurecontainerapps.io'
        $new = 'https://birrapoint-web.new.northeurope.azurecontainerapps.io'

        $updated = Get-SpaClientUpdate -Client $client -WebUrl $new

        $updated.rootUrl | Should -Be $new
        $updated.baseUrl | Should -Be $new
        $updated.redirectUris | Should -Be @("$new/*")
        $updated.webOrigins | Should -Be @($new)
        $updated.attributes.'post.logout.redirect.uris' | Should -Be "$new/*"
    }

    It 'keeps every other client setting untouched' {
        $client = New-SpaClient 'https://old.example'

        $updated = Get-SpaClientUpdate -Client $client -WebUrl 'https://new.example'

        $updated.id | Should -Be 'uuid-1'
        $updated.clientId | Should -Be 'birrapoint-spa'
        $updated.publicClient | Should -BeTrue
        $updated.attributes.'pkce.code.challenge.method' | Should -Be 'S256'
    }

    It 'does not modify the client object it was given' {
        $client = New-SpaClient 'https://old.example'

        Get-SpaClientUpdate -Client $client -WebUrl 'https://new.example' | Out-Null

        $client.rootUrl | Should -Be 'https://old.example'
        $client.attributes.'post.logout.redirect.uris' | Should -Be 'https://old.example/*'
    }

    It 'adds the logout attribute when the client has none' {
        $client = New-SpaClient 'https://old.example'
        $client.attributes = [pscustomobject]@{}

        (Get-SpaClientUpdate -Client $client -WebUrl 'https://new.example').attributes.'post.logout.redirect.uris' |
            Should -Be 'https://new.example/*'
    }
}
