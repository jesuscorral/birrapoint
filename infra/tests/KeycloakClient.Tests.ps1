# Pester 5+ tests for infra/KeycloakClient.psm1 (T140): keeping the SPA client's URLs in Keycloak in
# step with the web app's current URL, and the deploy service-account client.
# Run: Invoke-Pester infra/tests

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../KeycloakClient.psm1') -Force

    $script:Old = 'https://birrapoint-web.oldcoast-11111111.northeurope.azurecontainerapps.io'
    $script:New = 'https://birrapoint-web.newcoast-22222222.northeurope.azurecontainerapps.io'

    function New-SpaClient([string[]] $Urls) {
        [pscustomobject]@{
            id           = 'uuid-1'
            clientId     = 'birrapoint-spa'
            rootUrl      = $Urls[0]
            baseUrl      = $Urls[0]
            redirectUris = @($Urls | ForEach-Object { "$_/*" })
            webOrigins   = @($Urls)
            publicClient = $true
            attributes   = [pscustomobject]@{
                'post.logout.redirect.uris'  = (@($Urls | ForEach-Object { "$_/*" }) -join '##')
                'pkce.code.challenge.method' = 'S256'
            }
        }
    }
}

Describe 'Get-SpaClientUpdate' {
    It 'returns nothing when the client already allows the web URL' {
        Get-SpaClientUpdate -Client (New-SpaClient @($New)) -WebUrl $New | Should -BeNullOrEmpty
    }

    It 'ignores a trailing slash on the web URL' {
        Get-SpaClientUpdate -Client (New-SpaClient @($New)) -WebUrl "$New/" | Should -BeNullOrEmpty
    }

    It 'replaces the app''s previous Container Apps domain after the environment was recreated' {
        $updated = Get-SpaClientUpdate -Client (New-SpaClient @($Old)) -WebUrl $New

        $updated.rootUrl | Should -Be $New
        $updated.baseUrl | Should -Be $New
        $updated.redirectUris | Should -Be @("$New/*")
        $updated.webOrigins | Should -Be @($New)
        $updated.attributes.'post.logout.redirect.uris' | Should -Be "$New/*"
    }

    It 'keeps URIs added by hand (custom domain, localhost) and only swaps the managed domain' {
        $client = New-SpaClient @($Old, 'https://app.example.com', 'http://localhost:4200')

        $updated = Get-SpaClientUpdate -Client $client -WebUrl $New

        $updated.redirectUris | Should -Be @("$New/*", 'https://app.example.com/*', 'http://localhost:4200/*')
        $updated.webOrigins | Should -Be @($New, 'https://app.example.com', 'http://localhost:4200')
        $updated.attributes.'post.logout.redirect.uris' |
            Should -Be "$New/*##https://app.example.com/*##http://localhost:4200/*"
    }

    It 'never prunes another app''s Container Apps domain' {
        $other = 'https://someone-else.oldcoast-11111111.northeurope.azurecontainerapps.io'
        $updated = Get-SpaClientUpdate -Client (New-SpaClient @($Old, $other)) -WebUrl $New

        $updated.redirectUris | Should -Contain "$other/*"
        $updated.redirectUris | Should -Not -Contain "$Old/*"
    }

    It 'adds a custom-domain web URL without pruning anything' {
        $updated = Get-SpaClientUpdate -Client (New-SpaClient @($Old)) -WebUrl 'https://app.example.com'

        $updated.redirectUris | Should -Be @('https://app.example.com/*', "$Old/*")
    }

    It 'keeps every other client setting untouched and does not modify its input' {
        $client = New-SpaClient @($Old)

        $updated = Get-SpaClientUpdate -Client $client -WebUrl $New

        $updated.id | Should -Be 'uuid-1'
        $updated.publicClient | Should -BeTrue
        $updated.attributes.'pkce.code.challenge.method' | Should -Be 'S256'
        $client.rootUrl | Should -Be $Old
        $client.redirectUris | Should -Be @("$Old/*")
    }

    It 'adds the logout attribute when the client has none' {
        $client = New-SpaClient @($Old)
        $client.attributes = [pscustomobject]@{}

        (Get-SpaClientUpdate -Client $client -WebUrl $New).attributes.'post.logout.redirect.uris' | Should -Be "$New/*"
    }
}

Describe 'New-DeployClientRepresentation' {
    It 'describes a confidential, service-account-only client' {
        $client = New-DeployClientRepresentation -Secret 's3cret'

        $client.clientId | Should -Be 'birrapoint-deploy'
        $client.publicClient | Should -BeFalse
        $client.serviceAccountsEnabled | Should -BeTrue
        $client.standardFlowEnabled | Should -BeFalse
        $client.directAccessGrantsEnabled | Should -BeFalse
        $client.implicitFlowEnabled | Should -BeFalse
        $client.secret | Should -Be 's3cret'
    }
}
