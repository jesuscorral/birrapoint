# Pure helpers behind infra/deploy.ps1 (T134, FR-064): resolve which Docker Hub image each
# Container App should run, check it exists, and decide whether a rollout is needed. Kept free of
# Azure calls so it is unit-testable (infra/tests/DeployImages.Tests.ps1).
# Compatible with Windows PowerShell 5.1 and PowerShell 7.

$ErrorActionPreference = 'Stop'

# Component -> Container App name suffix; must match the locals in infra/terraform/main.tf.
$script:AppSuffixes = @{ api = 'api'; web = 'web'; keycloak = 'kc' }

function Resolve-ImageReference {
    # A release version X.Y.Z comes from the immutable birrapoint-<component>-release repository
    # (release.yml); no version means `latest` from the integration repository (ci.yml).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [ValidateNotNullOrEmpty()] [string] $Namespace,
        [Parameter(Mandatory = $true)] [ValidateSet('api', 'web', 'keycloak')] [string] $Component,
        [string] $Version
    )

    $pinned = -not [string]::IsNullOrWhiteSpace($Version)
    if ($pinned -and $Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw "Invalid $Component version '$Version': expected a release version X.Y.Z (e.g. 0.3.0), or omit it to deploy latest."
    }

    $repository = if ($pinned) { "birrapoint-$Component-release" } else { "birrapoint-$Component" }
    $tag = if ($pinned) { $Version } else { 'latest' }

    [pscustomobject]@{
        Component  = $Component
        Namespace  = $Namespace
        Repository = $repository
        Tag        = $tag
        Reference  = "docker.io/$Namespace/${repository}:$tag"
        Pinned     = $pinned
    }
}

function Get-ContainerAppName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $NamePrefix,
        [Parameter(Mandatory = $true)] [ValidateSet('api', 'web', 'keycloak')] [string] $Component
    )
    "$NamePrefix-$($script:AppSuffixes[$Component])"
}

function Get-HttpStatusCode {
    # Thin wrapper so Test-DockerHubTag's decision logic can be tested without the network.
    param([Parameter(Mandatory = $true)] [string] $Uri)
    try {
        $response = Invoke-WebRequest -Uri $Uri -Method Get -UseBasicParsing -TimeoutSec 30
        return [int] $response.StatusCode
    }
    catch {
        # PS 5.1 (WebException) and PS 7 (HttpResponseException) both expose Response.StatusCode.
        $response = $_.Exception.Response
        if ($response) { return [int] $response.StatusCode }
        throw
    }
}

function Test-DockerHubTag {
    # Anonymous Docker Hub API lookup (the repositories are public); no Docker daemon needed.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Namespace,
        [Parameter(Mandatory = $true)] [string] $Repository,
        [Parameter(Mandatory = $true)] [string] $Tag
    )
    $uri = "https://hub.docker.com/v2/namespaces/$Namespace/repositories/$Repository/tags/$Tag"
    $status = Get-HttpStatusCode -Uri $uri
    switch ($status) {
        200 { return $true }
        404 { return $false }
        default { throw "Docker Hub lookup of $Namespace/${Repository}:$Tag returned HTTP $status." }
    }
}

function New-RevisionSuffix {
    # A unique suffix forces a new revision even when the image reference is unchanged — the only
    # way to make Container Apps re-pull a moved `latest`. Lowercase alphanumerics and hyphens,
    # starting with a letter (Container Apps revision-suffix rules).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Tag,
        [datetime] $Timestamp = (Get-Date).ToUniversalTime()
    )
    $label = if ($Tag -eq 'latest') { 'latest' } else { 'v' + ($Tag -replace '\.', '-') }
    "$label-$($Timestamp.ToString('yyyyMMddHHmmss'))"
}

function Test-RolloutNeeded {
    # A pinned release already running is left alone (idempotent re-runs); `latest` always rolls
    # out because its content can change under the same reference.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] $Image,
        [string] $CurrentReference
    )
    -not ($Image.Pinned -and $CurrentReference -eq $Image.Reference)
}

Export-ModuleMember -Function Resolve-ImageReference, Get-ContainerAppName, Get-HttpStatusCode,
    Test-DockerHubTag, New-RevisionSuffix, Test-RolloutNeeded
