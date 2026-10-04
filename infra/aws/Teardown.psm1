# Pure helpers behind infra/aws/teardown.ps1 (T146): the confirmation check, resource names, which
# AWS resources and Neon project the teardown may delete, and the `terraform destroy` arguments.
# Kept free of AWS/Neon calls so they are unit-testable (infra/aws/tests/Teardown.Tests.ps1).
# Windows PowerShell 5.1 and PowerShell 7.

$ErrorActionPreference = 'Stop'

# Names of everything the sweep looks for; must match the locals in infra/aws/terraform/main.tf and
# the resources in alb.tf, ecs.tf, secrets.tf and cloudfront.tf (ADR-0021, ADR-0023).
# {0} is birrapoint-<environment>; LogGroupPrefix and the Neon project use the environment alone.
$script:ResourceNameFormats = @{
    Deployment            = '{0}'
    Vpc                   = '{0}-vpc'
    Alb                   = '{0}-alb'
    TargetGroupWeb        = '{0}-web-tg'
    TargetGroupKeycloak   = '{0}-kc-tg'
    EcsCluster            = '{0}-ecs'
    ServiceApi            = '{0}-api'
    ServiceWeb            = '{0}-web'
    ServiceKeycloak       = '{0}-kc'
    CloudMapNamespace     = '{0}.local'
    ExecutionRole         = '{0}-ecs-exec'
    TaskRoleApi           = '{0}-api-task'
    TaskRoleKeycloak      = '{0}-kc-task'
    DistributionWeb       = '{0}-web'
    DistributionKeycloak  = '{0}-kc'
    ResponseHeadersPolicy = '{0}-security'
    DockerHubSecret       = '{0}-dockerhub'
    NeonProject           = '{0}-neon'
}

# Application secrets have the generic names the apps ask Dapr for (secrets.tf), so they are NOT
# environment-qualified: only their tags say which deployment they belong to.
$script:GenericSecretNames = @(
    'ConnectionStrings--db',
    'ConnectionStrings--dbDirect',
    'Keycloak--AdminClientSecret',
    'keycloak-db-password',
    'keycloak-bootstrap-admin-password',
    'Smtp--Password'
)

function Test-TeardownConfirmation {
    # The operator must type the exact expected word (birrapoint-<environment>); anything else -
    # including "yes" or a different casing - aborts.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Expected,
        [AllowNull()] [AllowEmptyString()] [string] $Answer
    )
    if ($null -eq $Answer) { return $false }
    $Answer.Trim() -ceq $Expected
}

function ConvertTo-EnvironmentName {
    # Same rule as Terraform's `environment` variable: 2-10 letters/digits starting with a letter,
    # lower-cased.
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)] [AllowEmptyString()] [string] $Environment)
    if ($Environment -notmatch '^[A-Za-z][A-Za-z0-9]{1,9}$') {
        throw "Invalid environment '$Environment': 2-10 letters or digits, starting with a letter (e.g. PROD)."
    }
    $Environment.ToLowerInvariant()
}

function Get-TeardownResourceName {
    # The exact name of a resource of the deployment (see $script:ResourceNameFormats).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Environment,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Deployment', 'Vpc', 'Alb', 'TargetGroupWeb', 'TargetGroupKeycloak', 'EcsCluster',
            'ServiceApi', 'ServiceWeb', 'ServiceKeycloak', 'CloudMapNamespace', 'ExecutionRole',
            'TaskRoleApi', 'TaskRoleKeycloak', 'DistributionWeb', 'DistributionKeycloak',
            'ResponseHeadersPolicy', 'LogGroupPrefix', 'DockerHubSecret', 'NeonProject')]
        [string] $Resource
    )
    $name = ConvertTo-EnvironmentName $Environment
    if ($Resource -eq 'LogGroupPrefix') { return "/birrapoint/$name/" }
    $script:ResourceNameFormats[$Resource] -f "birrapoint-$name"
}

function Get-TeardownSecretName {
    # Every Secrets Manager secret name of the deployment: the generic application names plus the
    # environment-qualified Docker Hub credentials.
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)] [string] $Environment)
    @($script:GenericSecretNames) + (Get-TeardownResourceName -Environment $Environment -Resource DockerHubSecret)
}

function Get-TeardownTag {
    # The tags every resource of the deployment carries (provider default_tags in versions.tf).
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)] [string] $Environment)
    @{ application = 'birrapoint'; environment = (ConvertTo-EnvironmentName $Environment) }
}

function ConvertTo-TagMap {
    # Normalizes the tag shapes of the AWS CLI - [{Key,Value}], ECS's [{key,value}], CloudWatch
    # Logs' plain {name: value} map, a hashtable - into one case-sensitive dictionary (AWS tag keys
    # are case-sensitive).
    [CmdletBinding()]
    param([AllowNull()] $Tags)
    $map = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::Ordinal)
    if ($null -eq $Tags) { return $map }
    $items = if ($Tags -is [System.Collections.IDictionary]) { @(, $Tags) } else { @($Tags) }
    foreach ($item in $items) {
        if ($null -eq $item) { continue }
        if ($item -is [System.Collections.IDictionary]) {
            foreach ($key in $item.Keys) { $map[[string] $key] = [string] $item[$key] }
            continue
        }
        $keyProperty = $item.PSObject.Properties['Key']
        $valueProperty = $item.PSObject.Properties['Value']
        if ($keyProperty -and $valueProperty) {
            $map[[string] $keyProperty.Value] = [string] $valueProperty.Value
        }
        else {
            foreach ($property in $item.PSObject.Properties) { $map[$property.Name] = [string] $property.Value }
        }
    }
    $map
}

function Select-OwnedResource {
    # The only gate between a listing and a deletion: a resource is selected when its name matches
    # exactly (or starts with the exact -NamePrefix, for log groups) AND its tags include
    # application=birrapoint and environment=<environment>. Other environments, other applications
    # and untagged resources are never selected. The name is the resource's Name property, or the
    # Name tag when it has none (VPC). -IgnoreTags exists for the few types AWS cannot tag (CloudFront
    # response headers policies), where the exact name is the only criterion.
    [CmdletBinding()]
    param(
        [AllowNull()] [AllowEmptyCollection()] [object[]] $Resources = @(),
        [AllowNull()] [AllowEmptyCollection()] [string[]] $Names = @(),
        [string] $NamePrefix,
        [Parameter(Mandatory = $true)] [string] $Environment,
        [string] $NameProperty = 'Name',
        [switch] $IgnoreTags
    )
    $environmentName = ConvertTo-EnvironmentName $Environment
    $exactNames = @($Names | Where-Object { -not [string]::IsNullOrEmpty($_) })
    if ($exactNames.Count -eq 0 -and [string]::IsNullOrEmpty($NamePrefix)) {
        throw 'Select-OwnedResource needs at least one name or a name prefix; refusing to select every resource.'
    }
    foreach ($resource in @($Resources | Where-Object { $null -ne $_ })) {
        $tagProperty = $resource.PSObject.Properties['Tags']
        $tags = ConvertTo-TagMap -Tags $(if ($tagProperty) { $tagProperty.Value } else { $null })
        $nameMember = $resource.PSObject.Properties[$NameProperty]
        $name = if ($nameMember) { [string] $nameMember.Value } elseif ($tags.ContainsKey('Name')) { $tags['Name'] } else { $null }
        if ([string]::IsNullOrEmpty($name)) { continue }

        $nameMatches = ($exactNames -ccontains $name) -or
        (-not [string]::IsNullOrEmpty($NamePrefix) -and $name.StartsWith($NamePrefix, [System.StringComparison]::Ordinal))
        if (-not $nameMatches) { continue }

        if (-not $IgnoreTags) {
            if (-not ($tags.ContainsKey('application') -and $tags['application'] -ceq 'birrapoint')) { continue }
            if (-not ($tags.ContainsKey('environment') -and $tags['environment'] -ceq $environmentName)) { continue }
        }
        $resource
    }
}

function Select-OwnedSecret {
    # Secrets Manager secrets of this deployment: one of its secret names AND both deployment tags.
    # The tag check is what keeps a secret named ConnectionStrings--db of another environment or
    # application untouched.
    [CmdletBinding()]
    param(
        [AllowNull()] [AllowEmptyCollection()] [object[]] $Secrets = @(),
        [Parameter(Mandatory = $true)] [string] $Environment
    )
    Select-OwnedResource -Resources $Secrets -Names (Get-TeardownSecretName -Environment $Environment) -Environment $Environment
}

function Test-AwsNotFoundError {
    # True for the AWS CLI errors that mean "that resource does not exist", so a probe can treat them
    # as an answer; any other failure (access denied, throttling, no credentials) must surface.
    [CmdletBinding()]
    param([AllowNull()] [AllowEmptyString()] [string] $Message)
    if ([string]::IsNullOrWhiteSpace($Message)) { return $false }
    $Message -match 'NotFound|NoSuch'
}

function Get-VarFileRegion {
    # `region = "..."` of a tfvars file; comments and variables merely ending in "region" (neon_region)
    # are not matched.
    [CmdletBinding()]
    param([AllowNull()] [AllowEmptyString()] [string] $Content)
    if ([string]::IsNullOrWhiteSpace($Content)) { return $null }
    $match = [regex]::Match($Content, '(?m)^[ \t]*region[ \t]*=[ \t]*"([^"]+)"')
    if ($match.Success) { $match.Groups[1].Value.Trim() } else { $null }
}

function Resolve-AwsRegion {
    # -Region, then AWS_REGION, then AWS_DEFAULT_REGION, then the var file, then the Terraform
    # variable's own default.
    [CmdletBinding()]
    param(
        [string] $Explicit,
        [string] $EnvRegion,
        [string] $EnvDefaultRegion,
        [string] $VarFileRegion
    )
    foreach ($candidate in $Explicit, $EnvRegion, $EnvDefaultRegion, $VarFileRegion) {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) { return $candidate.Trim() }
    }
    'eu-central-1'
}

function Test-StateEnvironment {
    # Guards against destroying the wrong environment: the HCP workspace behind TF_WORKSPACE may
    # hold another environment than the one asked for. An empty state (nothing deployed) is fine.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Expected,
        [AllowNull()] [AllowEmptyString()] [string] $StateEnvironment
    )
    if ([string]::IsNullOrWhiteSpace($StateEnvironment)) { return $true }
    $StateEnvironment.Trim().ToLowerInvariant() -ceq $Expected.Trim().ToLowerInvariant()
}

function Resolve-StateEnvironment {
    # Decides what the HCP workspace holds from the results of `terraform state list` and
    # `terraform output -raw environment`. Returns the environment, or '' for an empty state
    # (nothing deployed yet). Any error reading a non-empty state throws: an unreadable state must
    # never be mistaken for an empty one.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [int] $StateListExitCode,
        [AllowNull()] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $StateList,
        [Parameter(Mandatory = $true)] [int] $OutputExitCode,
        [AllowNull()] [AllowEmptyString()] [string] $Output
    )
    if ($StateListExitCode -ne 0) {
        throw "Error reading the Terraform state (terraform state list exit code $StateListExitCode); nothing was changed."
    }
    $resources = @($StateList | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($resources.Count -eq 0) { return '' }
    if ($OutputExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($Output)) {
        throw 'The Terraform state has resources but no readable environment output; refusing to guess which environment it holds. Nothing was changed.'
    }
    $Output.Trim()
}

function Get-EnvironmentVarFile {
    # The committed, non-secret inputs of an environment, shared with deploy-aws.yml (ADR-0022).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $TerraformDir,
        [Parameter(Mandatory = $true)] [string] $Environment
    )
    [System.IO.Path]::Combine($TerraformDir, 'environments', ('{0}.tfvars' -f (ConvertTo-EnvironmentName $Environment)))
}

function Select-NeonProjectToDelete {
    # Only projects whose name is exactly the deployment's Neon project name; never a prefix or
    # substring match, so no other Neon project of the account is ever selected.
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()] [object[]] $Projects = @(),
        [Parameter(Mandatory = $true)] [string] $Name
    )
    @($Projects | Where-Object { $_.name -ceq $Name })
}

function Resolve-NeonProjectTarget {
    # Which Neon project the teardown deletes: the one with the exact name. Neon names are not
    # unique, so several matches are refused, never guessed between.
    [CmdletBinding()]
    param([AllowEmptyCollection()] [object[]] $NameMatches = @())
    $candidates = @($NameMatches)
    switch ($candidates.Count) {
        0 { return [pscustomobject]@{ Action = 'None'; ProjectId = $null } }
        1 { return [pscustomobject]@{ Action = 'DeleteById'; ProjectId = $candidates[0].id } }
        default { return [pscustomobject]@{ Action = 'Refuse'; ProjectId = $null } }
    }
}

function Resolve-NeonOrgId {
    # Neon requires org_id to list projects with a key that belongs to an organization. Order:
    # -NeonOrgId, then TF_VAR_neon_org_id (what Terraform uses), then the API key's only
    # organization. Several organizations are refused, never guessed between; none (a personal
    # account) means no org_id.
    [CmdletBinding()]
    param(
        [string] $Explicit,
        [string] $EnvValue,
        [AllowEmptyCollection()] [object[]] $Organizations = @()
    )
    if (-not [string]::IsNullOrWhiteSpace($Explicit)) { return $Explicit.Trim() }
    if (-not [string]::IsNullOrWhiteSpace($EnvValue)) { return $EnvValue.Trim() }
    $orgs = @($Organizations | Where-Object { $_ })
    switch ($orgs.Count) {
        0 { return $null }
        1 { return [string] $orgs[0].id }
        default {
            $ids = ($orgs | ForEach-Object { $_.id }) -join ', '
            throw "The Neon API key belongs to several organizations ($ids); pass -NeonOrgId or set TF_VAR_neon_org_id."
        }
    }
}

function Get-DestroyArgument {
    # `terraform destroy` arguments: always a full destroy (no -target). The var file comes first
    # so the -var flags win. The image namespace and release version are mandatory but meaningless
    # for a destroy, so they get placeholders, as do the required SMTP variables when there is no var
    # file. Credentials come from the AWS CLI's standard chain; the region only when given.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $TerraformDir,
        [Parameter(Mandatory = $true)] [string] $Environment,
        [string] $Region,
        [string] $VarFile
    )
    $arguments = @("-chdir=$TerraformDir", 'destroy', '-input=false', '-auto-approve')
    if ($VarFile) {
        $arguments += "-var-file=$VarFile"
    }
    $arguments += @(
        "-var=environment=$Environment",
        '-var=image_namespace=unused',
        # Required with no default (ADR-0022); meaningless for a destroy.
        '-var=release_version=latest'
    )
    if ($Region) {
        $arguments += "-var=region=$Region"
    }
    if (-not $VarFile) {
        $arguments += '-var=smtp_host=unused.invalid', '-var=smtp_from_address=unused@unused.invalid'
    }
    $arguments
}

Export-ModuleMember -Function Test-TeardownConfirmation, ConvertTo-EnvironmentName, Get-TeardownResourceName,
    Get-TeardownSecretName, Get-TeardownTag, ConvertTo-TagMap, Select-OwnedResource, Select-OwnedSecret,
    Test-AwsNotFoundError, Get-VarFileRegion, Resolve-AwsRegion,
    Select-NeonProjectToDelete, Resolve-NeonProjectTarget, Get-DestroyArgument,
    Test-StateEnvironment, Resolve-StateEnvironment, Get-EnvironmentVarFile, Resolve-NeonOrgId
