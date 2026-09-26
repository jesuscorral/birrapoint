# Pure helpers behind infra/deploy.ps1 (T134, FR-064, ADR-0018): resolve which Docker Hub image
# each Container App should run, check it exists, and make the rollout decisions (whether to roll
# out, how a revision is doing). Kept free of Azure calls so it is unit-testable
# (infra/tests/DeployImages.Tests.ps1). Compatible with Windows PowerShell 5.1 and PowerShell 7.

$ErrorActionPreference = 'Stop'

# Component -> Container App name suffix; must match the locals in infra/terraform/main.tf.
$script:AppSuffixes = @{ api = 'api'; web = 'web'; keycloak = 'kc' }

# Container Apps limit for a revision name (<app>--<suffix>).
$script:MaxRevisionNameLength = 64

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
    param(
        [Parameter(Mandatory = $true)] [string] $Uri,
        [hashtable] $Headers = @{}
    )
    try {
        $response = Invoke-WebRequest -Uri $Uri -Method Get -Headers $Headers -UseBasicParsing -TimeoutSec 30
        return [int] $response.StatusCode
    }
    catch {
        # PS 5.1 (WebException) and PS 7 (HttpResponseException) both expose Response.StatusCode.
        $response = $_.Exception.Response
        if ($response) { return [int] $response.StatusCode }
        throw
    }
}

function Get-DockerHubAuthHeader {
    # With DOCKERHUB_USERNAME + DOCKERHUB_TOKEN (a personal access token) in the environment, trade
    # them for a bearer token so private repositories can be checked; otherwise anonymous access.
    if (-not $env:DOCKERHUB_USERNAME -or -not $env:DOCKERHUB_TOKEN) { return @{} }

    $body = @{ identifier = $env:DOCKERHUB_USERNAME; secret = $env:DOCKERHUB_TOKEN } | ConvertTo-Json -Compress
    $response = Invoke-RestMethod -Uri 'https://hub.docker.com/v2/auth/token' -Method Post `
        -ContentType 'application/json' -Body $body -TimeoutSec 30
    if (-not $response.access_token) { throw 'Docker Hub authentication returned no access token.' }
    @{ Authorization = "Bearer $($response.access_token)" }
}

function Test-DockerHubTag {
    # Docker Hub API lookup; no Docker daemon needed.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Namespace,
        [Parameter(Mandatory = $true)] [string] $Repository,
        [Parameter(Mandatory = $true)] [string] $Tag
    )
    $uri = "https://hub.docker.com/v2/namespaces/$Namespace/repositories/$Repository/tags/$Tag"
    $status = Get-HttpStatusCode -Uri $uri -Headers (Get-DockerHubAuthHeader)
    switch ($status) {
        200 { return $true }
        404 { return $false }
        default { throw "Docker Hub lookup of $Namespace/${Repository}:$Tag returned HTTP $status." }
    }
}

function New-RevisionSuffix {
    # A unique suffix forces a new revision even when the image reference is unchanged — the only
    # way to make Container Apps re-pull a moved `latest` — and never reuses a suffix, which
    # Container Apps rejects. Rules: lowercase alphanumerics and single hyphens, starting with a
    # letter; the revision name <app>--<suffix> is at most 64 characters.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Tag,
        [Parameter(Mandatory = $true)] [string] $AppName,
        [datetime] $Timestamp = (Get-Date).ToUniversalTime()
    )
    $label = if ($Tag -match '^\d') { 'v' + ($Tag -replace '\.', '-') } else { $Tag.ToLowerInvariant() }
    $suffix = "$label-$($Timestamp.ToString('yyyyMMddHHmmss'))"
    $revisionName = "$AppName--$suffix"
    if ($revisionName.Length -gt $script:MaxRevisionNameLength) {
        throw "Revision name '$revisionName' exceeds the Container Apps limit of $($script:MaxRevisionNameLength) characters."
    }
    $suffix
}

function Test-RolloutNeeded {
    # Decides from what is actually serving, not from the app template: in Single revision mode a
    # failed rollout leaves the new image in the template while the previous revision keeps
    # serving. No rollout is needed only when the latest revision is the ready one and runs the
    # target image — for a pinned release always, for `latest` only when that revision was just
    # created (by the preceding infrastructure apply) and so has just pulled it.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] $Image,
        [string] $ReadyImage,
        [switch] $LatestIsReady,
        [switch] $JustRefreshed
    )
    $serving = $LatestIsReady -and $ReadyImage -eq $Image.Reference
    -not ($serving -and ($Image.Pinned -or $JustRefreshed))
}

function Get-RevisionOutcome {
    # Maps a revision's Container Apps state to Succeeded / Failed / Pending. Without health probes
    # Container Apps' default TCP probe decides `Healthy`; an app allowed to scale to zero (web)
    # may legitimately end provisioned with no replicas, and so no health state.
    [CmdletBinding()]
    param(
        [string] $ProvisioningState,
        [string] $RunningState,
        [string] $HealthState,
        [int] $Replicas,
        [int] $MinReplicas
    )
    if ($ProvisioningState -in 'Failed', 'Deprovisioning', 'Deprovisioned') { return 'Failed' }
    if ($RunningState -in 'Failed', 'Degraded') { return 'Failed' }
    if ($ProvisioningState -ne 'Provisioned') { return 'Pending' }
    if ($HealthState -eq 'Healthy') { return 'Succeeded' }
    if ($MinReplicas -eq 0 -and $Replicas -eq 0) { return 'Succeeded' }
    'Pending'
}

Export-ModuleMember -Function Resolve-ImageReference, Get-ContainerAppName, Get-HttpStatusCode,
    Get-DockerHubAuthHeader, Test-DockerHubTag, New-RevisionSuffix, Test-RolloutNeeded, Get-RevisionOutcome
