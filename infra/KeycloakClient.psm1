# Pure helpers behind infra/deploy.ps1's Keycloak step (T140).
#
# Keycloak imports the realm only on first start (an existing realm in its database is never
# re-imported). After the Container Apps environment is recreated - a new random default domain -
# the stored birrapoint-spa client would still allow only the old URL and every login would fail
# with "Invalid parameter: redirect_uri". deploy.ps1 therefore reconciles the client's URLs after
# each full run, authenticating as the birrapoint-deploy service-account client described here.
# Unit-tested in infra/tests/KeycloakClient.Tests.ps1. Windows PowerShell 5.1 and PowerShell 7.

$ErrorActionPreference = 'Stop'

$script:LogoutAttribute = 'post.logout.redirect.uris'
# Keycloak stores several post-logout redirect URIs in one attribute, separated by "##".
$script:LogoutSeparator = '##'

function Get-ManagedUrlPattern([string] $WebUrl) {
    # Regex matching this web app's own Container Apps URLs on ANY environment domain
    # (https://<app>.<env-domain>.<region>.azurecontainerapps.io), or $null when the web URL is
    # not a Container Apps one (custom domain): then nothing is ever pruned.
    $uri = [uri] $WebUrl
    if ($uri.Host -notmatch '^([a-z0-9-]+)\.[a-z0-9-]+\.[a-z0-9-]+\.azurecontainerapps\.io$') { return $null }
    '^https://' + [regex]::Escape($Matches[1]) + '\.[a-z0-9-]+\.[a-z0-9-]+\.azurecontainerapps\.io(/.*)?$'
}

function Merge-UrlList([string[]] $Current, [string] $Wanted, [string] $ManagedPattern) {
    # Wanted first; then every existing entry except duplicates and this app's stale managed URLs.
    $result = New-Object System.Collections.Generic.List[string]
    $result.Add($Wanted)
    foreach ($entry in @($Current)) {
        if (-not $entry -or $entry -eq $Wanted -or $result.Contains($entry)) { continue }
        if ($ManagedPattern -and $entry -match $ManagedPattern) { continue }
        $result.Add($entry)
    }
    , $result.ToArray()
}

function Test-SameSet([string[]] $Left, [string[]] $Right) {
    $l = @($Left | Where-Object { $_ } | Sort-Object -Unique)
    $r = @($Right | Where-Object { $_ } | Sort-Object -Unique)
    ($l -join '|') -ceq ($r -join '|')
}

function Get-SpaClientUpdate {
    # Returns a copy of the client representation that allows the web URL - rootUrl/baseUrl set to
    # it; redirect URIs, web origins and post-logout URIs MERGED: the current URL is added and only
    # this app's own previous Container Apps domains are dropped, so URIs added by hand (a custom
    # domain, localhost) survive. Returns nothing when already in sync (no Keycloak write).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] $Client,
        [Parameter(Mandatory = $true)] [string] $WebUrl
    )
    $url = $WebUrl.TrimEnd('/')
    $wildcard = "$url/*"
    $managed = Get-ManagedUrlPattern $url

    $currentLogout = if ($Client.attributes -and $Client.attributes.$script:LogoutAttribute) {
        @($Client.attributes.$script:LogoutAttribute -split [regex]::Escape($script:LogoutSeparator))
    }
    else { @() }

    $redirects = Merge-UrlList @($Client.redirectUris) $wildcard $managed
    $origins = Merge-UrlList @($Client.webOrigins) $url $managed
    $logout = Merge-UrlList $currentLogout $wildcard $managed

    # Keycloak stores these as sets and may return them in any order: compare order-insensitively.
    $inSync = $Client.rootUrl -eq $url -and $Client.baseUrl -eq $url -and
        (Test-SameSet @($Client.redirectUris) $redirects) -and
        (Test-SameSet @($Client.webOrigins) $origins) -and
        (Test-SameSet $currentLogout $logout)
    if ($inSync) { return }

    # Deep copy through JSON: the caller's object stays untouched.
    $updated = $Client | ConvertTo-Json -Depth 20 | ConvertFrom-Json
    $updated.rootUrl = $url
    $updated.baseUrl = $url
    $updated.redirectUris = $redirects
    $updated.webOrigins = $origins
    if (-not $updated.attributes) {
        $updated | Add-Member -NotePropertyName attributes -NotePropertyValue ([pscustomobject]@{}) -Force
    }
    $updated.attributes | Add-Member -NotePropertyName $script:LogoutAttribute `
        -NotePropertyValue ($logout -join $script:LogoutSeparator) -Force
    $updated
}

function New-DeployClientRepresentation {
    # The birrapoint-deploy client: confidential, service account only (client_credentials), no
    # browser or password flows. Its service account gets realm-management view-clients and
    # manage-clients in the birrapoint realm only - enough to reconcile birrapoint-spa, nothing in
    # master. Used to (re)create it on realms imported before it existed.
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)] [string] $Secret)
    [pscustomobject]@{
        clientId                  = 'birrapoint-deploy'
        name                      = 'BirraPoint deployment (infra/deploy.ps1)'
        enabled                   = $true
        publicClient              = $false
        clientAuthenticatorType   = 'client-secret'
        secret                    = $Secret
        serviceAccountsEnabled    = $true
        standardFlowEnabled       = $false
        implicitFlowEnabled       = $false
        directAccessGrantsEnabled = $false
        protocol                  = 'openid-connect'
    }
}

function Get-RoleMappingChange {
    # Client-role changes that make a service account hold exactly the wanted roles: the missing
    # ones to add and any others (e.g. granted by hand) to remove. Compared by role name.
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()] [object[]] $Current = @(),
        [AllowEmptyCollection()] [object[]] $Wanted = @()
    )
    $currentNames = @($Current | ForEach-Object { $_.name })
    $wantedNames = @($Wanted | ForEach-Object { $_.name })
    [pscustomobject]@{
        Add    = @($Wanted | Where-Object { $currentNames -notcontains $_.name })
        Remove = @($Current | Where-Object { $wantedNames -notcontains $_.name })
    }
}

function ConvertTo-JsonArray {
    # A plain JSON array of the items. Windows PowerShell 5.1 serializes an array that lives in an
    # object property (e.g. Get-RoleMappingChange's Add/Remove) as {"value":[...],"Count":n},
    # which the Keycloak Admin API rejects; building the array text explicitly avoids that.
    [CmdletBinding()]
    param([AllowEmptyCollection()] [object[]] $Items = @())
    $elements = @($Items | ForEach-Object { ConvertTo-Json -InputObject $_ -Depth 10 -Compress })
    '[' + ($elements -join ',') + ']'
}

function ConvertTo-ItemList {
    # Emits the items of a REST response. Windows PowerShell 5.1's Invoke-RestMethod (like its
    # ConvertFrom-Json) returns a JSON array as ONE object, so `@(Invoke-RestMethod ...)` would be a
    # one-element list holding the whole array; PowerShell 7 enumerates. Assign the response to a
    # variable and pass it here instead.
    param($Response)
    if ($null -eq $Response) { return }
    foreach ($item in @($Response)) { $item }
}

Export-ModuleMember -Function Get-SpaClientUpdate, New-DeployClientRepresentation, Get-RoleMappingChange,
    ConvertTo-JsonArray, ConvertTo-ItemList
