# Pure helper behind infra/deploy.ps1's Keycloak step (T140): keeps the SPA client's URLs in step
# with the web app's current URL. Keycloak imports the realm only on first start (an existing realm
# in its database is never re-imported), so after the Container Apps environment is recreated - a
# new random default domain - the stored client would still allow only the old URL and every login
# would fail with "Invalid parameter: redirect_uri". Unit-tested in
# infra/tests/KeycloakClient.Tests.ps1. Windows PowerShell 5.1 and PowerShell 7.

$ErrorActionPreference = 'Stop'

$script:LogoutAttribute = 'post.logout.redirect.uris'

function Get-SpaClientUpdate {
    # Returns a copy of the client representation with rootUrl, baseUrl, redirectUris, webOrigins
    # and the post-logout redirect URIs set to the web URL - or nothing when they already match,
    # so an unchanged environment makes no Keycloak write. Every other setting is kept as is.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] $Client,
        [Parameter(Mandatory = $true)] [string] $WebUrl
    )
    $url = $WebUrl.TrimEnd('/')
    $wildcard = "$url/*"

    $currentLogout = if ($Client.attributes) { $Client.attributes.$script:LogoutAttribute } else { $null }
    $inSync = $Client.rootUrl -eq $url -and $Client.baseUrl -eq $url -and
        (@($Client.redirectUris) -join '|') -eq $wildcard -and
        (@($Client.webOrigins) -join '|') -eq $url -and
        $currentLogout -eq $wildcard
    if ($inSync) { return }

    # Deep copy through JSON: the caller's object stays untouched.
    $updated = $Client | ConvertTo-Json -Depth 20 | ConvertFrom-Json
    $updated.rootUrl = $url
    $updated.baseUrl = $url
    $updated.redirectUris = @($wildcard)
    $updated.webOrigins = @($url)
    if (-not $updated.attributes) {
        $updated | Add-Member -NotePropertyName attributes -NotePropertyValue ([pscustomobject]@{}) -Force
    }
    $updated.attributes | Add-Member -NotePropertyName $script:LogoutAttribute -NotePropertyValue $wildcard -Force
    $updated
}

Export-ModuleMember -Function Get-SpaClientUpdate
