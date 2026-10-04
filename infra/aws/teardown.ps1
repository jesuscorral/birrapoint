<#
.SYNOPSIS
    Removes ABSOLUTELY EVERYTHING of the BirraPoint AWS environment - AWS and Neon, data included -
    so the next deploy starts from scratch (T146). Independent from infra/azure/teardown.ps1.

.DESCRIPTION
    Always a full wipe, no data is kept:
      1. `terraform init` against the HCP Terraform workspace, and a guard: the workspace must hold
         the requested environment (or be empty), otherwise nothing is changed;
      2. a plan of the removal, then a typed confirmation of birrapoint-<environment> (-Force skips it;
         -WhatIf changes nothing), then `terraform destroy` of everything Terraform manages;
      3. a sweep of whatever the destroy left behind, found by exact name AND the deployment tags
         (application=birrapoint, environment=<environment>) - another environment's or application's
         resources are never touched: Secrets Manager secrets (deleted without a recovery window),
         log groups, ECS services and cluster, the ALB with its listeners and target groups, Cloud Map
         services and namespace, IAM roles, CloudFront distributions (disabled, then deleted: ~15
         minutes) and the response headers policy, the VPC with its subnets, route tables, internet
         gateway and security groups, and the Neon project whose id is in the Terraform state (its name
         must be birrapoint-<environment>-aws-neon; never chosen by name alone, see -NeonProjectId);
      4. removal of the local infra/aws/terraform/.terraform folder, then a verification through the
         resource groups tagging API and direct probes of the IAM roles: anything still there for the
         deployment is listed and the script exits with an error.
    Docker Hub images, GitHub secrets/variables, the IAM OIDC provider and deploy role, and the HCP
    Terraform workspace itself are not touched.

    Idempotent: safe to re-run after a partial failure. Supports -WhatIf. Compatible with Windows
    PowerShell 5.1 and PowerShell 7.

    Prerequisites: AWS CLI v2 signed in (`aws sso login` or access keys), Terraform >= 1.9, HCP
    Terraform access (`terraform login` or TF_TOKEN_app_terraform_io) with TF_CLOUD_ORGANIZATION and
    TF_WORKSPACE set (the AWS workspace), NEON_API_KEY. The region is -Region, then AWS_REGION /
    AWS_DEFAULT_REGION, then the var file's region.

.EXAMPLE
    ./infra/aws/teardown.ps1 -WhatIf
    Shows what would be removed and changes nothing.
.EXAMPLE
    ./infra/aws/teardown.ps1
    Deletes everything, data included, after asking for confirmation.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    # Deployment environment (resources are named birrapoint-<environment>-<acronym>).
    [string] $Environment = 'PROD',

    # Defaults to infra/aws/terraform/environments/<environment>.tfvars when it exists; Terraform also
    # loads the gitignored infra/aws/terraform/terraform.tfvars on its own.
    [string] $VarFile,

    # AWS region of the deployment. Defaults to AWS_REGION / AWS_DEFAULT_REGION, then the var file.
    # The resolved region is always passed to Terraform, so the destroy and the sweep agree.
    [string] $Region,

    # Neon organization id. Defaults to TF_VAR_neon_org_id, then to the API key's only organization.
    [string] $NeonOrgId,

    # Neon project id to delete when the Terraform state is empty (e.g. after a destroy that failed
    # half way). Without it an empty state deletes no Neon project: name matches are only reported.
    # The project's name must still be birrapoint-<environment>-aws-neon.
    [ValidatePattern('^[a-z0-9-]*$')]
    [string] $NeonProjectId,

    # Skips the typed confirmation. For deliberate, scripted use only.
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$terraformDir = Join-Path $PSScriptRoot 'terraform'
Import-Module (Join-Path $PSScriptRoot 'Teardown.psm1') -Force
$environmentName = ConvertTo-EnvironmentName $Environment
# Resolved here, not as a param default: Windows PowerShell 5.1 leaves $PSScriptRoot empty in
# param defaults when the script is run with `powershell -File`.
if (-not $VarFile) {
    $defaultVarFile = Get-EnvironmentVarFile -TerraformDir $terraformDir -Environment $environmentName
    if (Test-Path $defaultVarFile) { $VarFile = $defaultVarFile }
}

# The functions below call ShouldProcess through the script's own cmdlet context so -WhatIf applies.
$script:ScriptCmdlet = $PSCmdlet
$script:AwsRegion = $null
$script:StateNeonProjectId = ''

$neonApi = 'https://console.neon.tech/api/v2'
$neonHeaders = @{ Authorization = "Bearer $env:NEON_API_KEY"; Accept = 'application/json' }

# --- Helpers ------------------------------------------------------------------------------------

function Invoke-Native {
    # Runs a native command and throws on a non-zero exit code (PowerShell 5.1 does not).
    param([string] $Description, [scriptblock] $Command)
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Description failed (exit code $LASTEXITCODE)." }
}

function Assert-Command([string] $Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Prerequisite '$Name' is not on PATH (see infra/aws/terraform/README.md)."
    }
}

# Windows PowerShell 5.1 turns a native command's redirected stderr into terminating errors
# under ErrorActionPreference=Stop, so probes whose failure is an expected answer run with
# 'Continue' locally.
function Invoke-Probe([scriptblock] $Command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Command 2>$null } finally { $ErrorActionPreference = $previous }
}

function Invoke-Aws {
    # Runs `aws <arguments> --region <region> --output json` and returns the parsed JSON ($null for no
    # output). Throws on a non-zero exit code; with -Probe, a "not found" answer returns $null instead
    # (any other failure - access denied, throttling - still throws). Output is never echoed: it can
    # hold secret metadata.
    param(
        [Parameter(Mandatory = $true)] [string[]] $Arguments,
        [switch] $Probe,
        [string] $RegionOverride
    )
    $regionToUse = if ($RegionOverride) { $RegionOverride } else { $script:AwsRegion }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = @(& aws @Arguments --region $regionToUse --output json 2>&1) }
    finally { $ErrorActionPreference = $previous }
    $exitCode = $LASTEXITCODE
    $errorText = (@($output | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { $_.ToString() })) -join "`n"
    $text = (@($output | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] })) -join "`n"
    if ($exitCode -ne 0) {
        if ($Probe -and (Test-AwsNotFoundError -Message $errorText)) { return $null }
        throw "aws $($Arguments[0]) $($Arguments[1]) failed (exit code $exitCode): $errorText"
    }
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    $text | ConvertFrom-Json
}

function ConvertTo-List($Value) {
    # A list from an AWS response: no null elements, always an array.
    @($Value | Where-Object { $null -ne $_ })
}

function Confirm-Action([string] $Target, [string] $Action) {
    $script:ScriptCmdlet.ShouldProcess($Target, $Action)
}

function Invoke-Step {
    # A failing step is recorded and the next one still runs, so one stuck resource does not stop
    # the rest of the sweep; the final verification reports what remains.
    param([string] $Name, [scriptblock] $Body)
    try { & $Body }
    catch {
        Write-Warning "$Name failed: $($_.Exception.Message)"
        $script:Problems += "$Name failed."
    }
}

function Get-NeonOrganization {
    # The API key's organizations. An organization-scoped key cannot list them; it gets none here
    # and then needs -NeonOrgId or TF_VAR_neon_org_id when Neon asks for org_id.
    try {
        $response = Invoke-RestMethod -Uri "$neonApi/users/me/organizations" -Headers $neonHeaders -TimeoutSec 30
        @($response.organizations)
    }
    catch {
        @()
    }
}

function Get-NeonProjectById([string] $Id) {
    # The project with that id, or $null when Neon answers 404; any other failure throws.
    try {
        $response = Invoke-RestMethod -Uri "$neonApi/projects/$([uri]::EscapeDataString($Id))" -Headers $neonHeaders -TimeoutSec 30
        $response.project
    }
    catch {
        $status = $null
        if ($_.Exception.Response) { $status = [int] $_.Exception.Response.StatusCode }
        if ($status -eq 404) { return $null }
        throw
    }
}

function Get-NeonDeletionTarget {
    # What the teardown may do with Neon right now (see Resolve-NeonDeletionTarget): the project id
    # comes from the Terraform state or -NeonProjectId, its name is verified against the Neon API,
    # and a name match alone never selects anything.
    $uri = "$neonApi/projects?limit=400&search=$([uri]::EscapeDataString($neonProjectName))"
    if ($NeonOrgId) { $uri += "&org_id=$([uri]::EscapeDataString($NeonOrgId))" }
    $found = @((Invoke-RestMethod -Uri $uri -Headers $neonHeaders -TimeoutSec 30).projects)
    $targetId = if ($script:StateNeonProjectId) { $script:StateNeonProjectId } else { $NeonProjectId }
    if ($targetId -and -not ($found | Where-Object { $_.id -ceq $targetId })) {
        $byId = Get-NeonProjectById $targetId
        if ($byId) { $found += $byId }
    }
    Resolve-NeonDeletionTarget -StateProjectId $script:StateNeonProjectId -ExplicitProjectId $NeonProjectId `
        -ProjectsFound $found -ExpectedName $neonProjectName
}

function Read-Confirmation([string] $Prompt, [string] $Expected) {
    $answer = Read-Host "$Prompt Type '$Expected' to continue"
    if (-not (Test-TeardownConfirmation -Expected $Expected -Answer $answer)) {
        throw 'Not confirmed; nothing was changed.'
    }
}

# --- Inventory: what exists, per type, selected by exact name AND deployment tags ---------------

function Get-AwsInventory {
    # Read-only. Every list is narrowed to exact names first (fewer tag calls), and Select-Owned*
    # then enforces name + application/environment tags, so nothing foreign is ever selected.
    $name = { param($resource) Get-TeardownResourceName -Environment $environmentName -Resource $resource }

    # Secrets already scheduled for deletion are included: they still hold the name.
    $secretList = Invoke-Aws @('secretsmanager', 'list-secrets', '--include-planned-deletion')
    $secrets = foreach ($s in (ConvertTo-List $secretList.SecretList)) {
        [pscustomobject]@{ Name = $s.Name; Id = $s.ARN; Tags = $s.Tags }
    }

    $prefix = & $name LogGroupPrefix
    $groupList = Invoke-Aws @('logs', 'describe-log-groups', '--log-group-name-prefix', $prefix)
    $groups = foreach ($g in (ConvertTo-List $groupList.logGroups)) {
        if (-not $g.logGroupName.StartsWith($prefix, [System.StringComparison]::Ordinal)) { continue }
        $arn = $g.arn -replace ':\*$', ''
        $tagResponse = Invoke-Aws @('logs', 'list-tags-for-resource', '--resource-arn', $arn)
        [pscustomobject]@{ Name = $g.logGroupName; Id = $g.logGroupName; Tags = $tagResponse.tags }
    }

    $clusterName = & $name EcsCluster
    $clusterResponse = Invoke-Aws @('ecs', 'describe-clusters', '--clusters', $clusterName, '--include', 'TAGS')
    $clusters = foreach ($c in (ConvertTo-List $clusterResponse.clusters)) {
        if ($c.status -ne 'ACTIVE') { continue }
        [pscustomobject]@{ Name = $c.clusterName; Id = $c.clusterArn; Tags = $c.tags }
    }
    $ownedClusters = @(Select-OwnedResource -Resources @($clusters) -Names $clusterName -Environment $environmentName)

    $serviceNames = @((& $name ServiceApi), (& $name ServiceWeb), (& $name ServiceKeycloak))
    $services = @()
    if ($ownedClusters.Count -gt 0) {
        $serviceResponse = Invoke-Aws -Probe (@('ecs', 'describe-services', '--cluster', $clusterName, '--services') + $serviceNames + @('--include', 'TAGS'))
        $services = foreach ($s in (ConvertTo-List $serviceResponse.services)) {
            # ACTIVE ones are deleted; DRAINING ones (a partial destroy) only awaited.
            if ($s.status -notin 'ACTIVE', 'DRAINING') { continue }
            [pscustomobject]@{ Name = $s.serviceName; Id = $s.serviceArn; Status = $s.status; Tags = $s.tags }
        }
    }

    $albName = & $name Alb
    $albResponse = Invoke-Aws -Probe @('elbv2', 'describe-load-balancers', '--names', $albName)
    $balancers = foreach ($lb in (ConvertTo-List $albResponse.LoadBalancers)) {
        $tagResponse = Invoke-Aws @('elbv2', 'describe-tags', '--resource-arns', $lb.LoadBalancerArn)
        [pscustomobject]@{ Name = $lb.LoadBalancerName; Id = $lb.LoadBalancerArn; Tags = @($tagResponse.TagDescriptions)[0].Tags }
    }

    $targetGroupNames = @((& $name TargetGroupWeb), (& $name TargetGroupKeycloak))
    $targetGroups = foreach ($tgName in $targetGroupNames) {
        # One call per name: describe-target-groups fails as a whole when any name is missing.
        $tgResponse = Invoke-Aws -Probe @('elbv2', 'describe-target-groups', '--names', $tgName)
        foreach ($tg in (ConvertTo-List $tgResponse.TargetGroups)) {
            $tagResponse = Invoke-Aws @('elbv2', 'describe-tags', '--resource-arns', $tg.TargetGroupArn)
            [pscustomobject]@{ Name = $tg.TargetGroupName; Id = $tg.TargetGroupArn; Tags = @($tagResponse.TagDescriptions)[0].Tags }
        }
    }

    $namespaceName = & $name CloudMapNamespace
    $namespaceList = Invoke-Aws @('servicediscovery', 'list-namespaces')
    $namespaces = foreach ($n in (ConvertTo-List $namespaceList.Namespaces)) {
        if ($n.Name -cne $namespaceName) { continue }
        $tagResponse = Invoke-Aws @('servicediscovery', 'list-tags-for-resource', '--resource-arn', $n.Arn)
        [pscustomobject]@{ Name = $n.Name; Id = $n.Id; Tags = $tagResponse.Tags }
    }

    $roleNames = @((& $name ExecutionRole), (& $name TaskRoleApi), (& $name TaskRoleKeycloak))
    $roles = foreach ($roleName in $roleNames) {
        $roleResponse = Invoke-Aws -Probe @('iam', 'get-role', '--role-name', $roleName)
        if ($roleResponse) {
            [pscustomobject]@{ Name = $roleResponse.Role.RoleName; Id = $roleResponse.Role.RoleName; Tags = $roleResponse.Role.Tags }
        }
    }

    $distributionNames = @((& $name DistributionWeb), (& $name DistributionKeycloak))
    $distributionList = Invoke-Aws @('cloudfront', 'list-distributions')
    $distributions = foreach ($d in (ConvertTo-List $distributionList.DistributionList.Items)) {
        if ($distributionNames -cnotcontains $d.Comment) { continue }
        $tagResponse = Invoke-Aws @('cloudfront', 'list-tags-for-resource', '--resource', $d.ARN)
        [pscustomobject]@{ Name = $d.Comment; Id = $d.Id; Enabled = $d.Enabled; Tags = $tagResponse.Tags.Items }
    }

    $policyName = & $name ResponseHeadersPolicy
    $policyList = Invoke-Aws @('cloudfront', 'list-response-headers-policies', '--type', 'custom')
    $policies = foreach ($p in (ConvertTo-List $policyList.ResponseHeadersPolicyList.Items)) {
        [pscustomobject]@{ Name = $p.ResponseHeadersPolicy.ResponseHeadersPolicyConfig.Name; Id = $p.ResponseHeadersPolicy.Id }
    }

    $vpcName = & $name Vpc
    $vpcResponse = Invoke-Aws @('ec2', 'describe-vpcs', '--filters',
        "Name=tag:Name,Values=$vpcName", 'Name=tag:application,Values=birrapoint', "Name=tag:environment,Values=$environmentName")
    $vpcs = foreach ($v in (ConvertTo-List $vpcResponse.Vpcs)) {
        [pscustomobject]@{ Id = $v.VpcId; Tags = $v.Tags }
    }

    [pscustomobject]@{
        Secrets       = @(Select-OwnedSecret -Secrets @($secrets) -Environment $environmentName)
        LogGroups     = @(Select-OwnedResource -Resources @($groups) -NamePrefix $prefix -Environment $environmentName)
        EcsCluster    = $ownedClusters
        EcsServices   = @(Select-OwnedResource -Resources @($services) -Names ($serviceNames) -Environment $environmentName)
        LoadBalancers = @(Select-OwnedResource -Resources @($balancers) -Names $albName -Environment $environmentName)
        TargetGroups  = @(Select-OwnedResource -Resources @($targetGroups) -Names $targetGroupNames -Environment $environmentName)
        Namespaces    = @(Select-OwnedResource -Resources @($namespaces) -Names $namespaceName -Environment $environmentName)
        Roles         = @(Select-OwnedResource -Resources @($roles) -Names $roleNames -Environment $environmentName)
        Distributions = @(Select-OwnedResource -Resources @($distributions) -Names $distributionNames -Environment $environmentName)
        # Response headers policies cannot be tagged: the exact name is the only criterion.
        Policies      = @(Select-OwnedResource -Resources @($policies) -Names $policyName -Environment $environmentName -IgnoreTags)
        Vpcs          = @(Select-OwnedResource -Resources @($vpcs) -Names $vpcName -Environment $environmentName)
    }
}

function Write-Inventory($Inventory) {
    $rows = @(
        @('Secrets Manager secrets', $Inventory.Secrets, 'Name'),
        @('CloudWatch log groups', $Inventory.LogGroups, 'Name'),
        @('ECS cluster', $Inventory.EcsCluster, 'Name'),
        @('ECS services', $Inventory.EcsServices, 'Name'),
        @('Load balancers', $Inventory.LoadBalancers, 'Name'),
        @('Target groups', $Inventory.TargetGroups, 'Name'),
        @('Cloud Map namespaces', $Inventory.Namespaces, 'Name'),
        @('IAM roles', $Inventory.Roles, 'Name'),
        @('CloudFront distributions', $Inventory.Distributions, 'Id'),
        @('CloudFront response headers policies', $Inventory.Policies, 'Name'),
        @('VPCs', $Inventory.Vpcs, 'Id')
    )
    foreach ($row in $rows) {
        $items = @($row[1])
        $names = ($items | ForEach-Object { $_.($row[2]) }) -join ', '
        if ($items.Count -gt 0) { Write-Host ("  - {0}: {1}" -f $row[0], $names) }
        else { Write-Host ("  - {0}: none found" -f $row[0]) }
    }
}

# --- Removal steps (each one is idempotent and guarded by ShouldProcess) --------------------------

function New-PrivateDirectory {
    # A new temp directory readable only by the current user (an NTFS ACL without inherited rules on
    # Windows, mode 700 elsewhere).
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ('birrapoint-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path | Out-Null
    if ($PSVersionTable.PSEdition -eq 'Desktop' -or $IsWindows) {
        $acl = Get-Acl -LiteralPath $path
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($rule in @($acl.Access)) { [void] $acl.RemoveAccessRule($rule) }
        $user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($user, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow')))
        Set-Acl -LiteralPath $path -AclObject $acl
    }
    else {
        & chmod 700 $path
    }
    $path
}

function Start-DistributionDisable($Distribution) {
    # Disabling takes ~15 minutes to deploy, so it starts first and runs while the rest is removed.
    if ($Distribution.Enabled -eq $false) { return }
    if (-not (Confirm-Action "CloudFront distribution $($Distribution.Id)" 'Disable')) { return }
    Write-Host "==> Disable CloudFront distribution $($Distribution.Id)" -ForegroundColor Cyan
    $current = Invoke-Aws @('cloudfront', 'get-distribution-config', '--id', $Distribution.Id)
    $current.DistributionConfig.Enabled = $false
    # The configuration holds the secret origin-verify header value: it goes into a directory only the
    # current user can read, removed right after the call.
    $directory = New-PrivateDirectory
    $file = Join-Path $directory 'distribution-config.json'
    try {
        [System.IO.File]::WriteAllText($file, ($current.DistributionConfig | ConvertTo-Json -Depth 30), (New-Object System.Text.UTF8Encoding($false)))
        Invoke-Aws @('cloudfront', 'update-distribution', '--id', $Distribution.Id, '--if-match', $current.ETag,
            '--distribution-config', ('file://' + ($file -replace '\\', '/'))) | Out-Null
    }
    finally { Remove-Item -Recurse -Force $directory -ErrorAction SilentlyContinue }
}

function Remove-Distribution($Distribution) {
    if (-not (Confirm-Action "CloudFront distribution $($Distribution.Id)" 'Wait until disabled, then delete')) { return }
    Write-Host "==> Wait for CloudFront distribution $($Distribution.Id) to be deployed (disabled); this can take ~15 minutes" -ForegroundColor Cyan
    Invoke-Aws @('cloudfront', 'wait', 'distribution-deployed', '--id', $Distribution.Id) | Out-Null
    $current = Invoke-Aws @('cloudfront', 'get-distribution-config', '--id', $Distribution.Id)
    Write-Host "==> Delete CloudFront distribution $($Distribution.Id)" -ForegroundColor Cyan
    Invoke-Aws @('cloudfront', 'delete-distribution', '--id', $Distribution.Id, '--if-match', $current.ETag) | Out-Null
}

function Remove-ResponseHeadersPolicy($Policy) {
    if (-not (Confirm-Action "CloudFront response headers policy $($Policy.Name)" 'Delete')) { return }
    Write-Host "==> Delete CloudFront response headers policy $($Policy.Name)" -ForegroundColor Cyan
    $current = Invoke-Aws @('cloudfront', 'get-response-headers-policy', '--id', $Policy.Id)
    Invoke-Aws @('cloudfront', 'delete-response-headers-policy', '--id', $Policy.Id, '--if-match', $current.ETag) | Out-Null
}

function Remove-EcsServices($Inventory) {
    $clusterName = $Inventory.EcsCluster[0].Name
    # Services still draining (left by a partial destroy) cannot be updated or deleted again, but the
    # cluster cannot be deleted until they are inactive: they are only awaited. Services the operator
    # declined (-Confirm) are not awaited either.
    $awaited = @()
    foreach ($service in $Inventory.EcsServices) {
        if ($service.Status -eq 'DRAINING') { $awaited += $service.Name; continue }
        if (-not (Confirm-Action "ECS service $($service.Name)" 'Scale to 0 and delete')) { continue }
        Write-Host "==> Delete ECS service $($service.Name)" -ForegroundColor Cyan
        Invoke-Aws @('ecs', 'update-service', '--cluster', $clusterName, '--service', $service.Name, '--desired-count', '0') | Out-Null
        Invoke-Aws @('ecs', 'delete-service', '--cluster', $clusterName, '--service', $service.Name, '--force') | Out-Null
        $awaited += $service.Name
    }
    if ($awaited.Count -gt 0 -and -not $WhatIfPreference) {
        Write-Host '==> Wait for the ECS services to become inactive' -ForegroundColor Cyan
        Invoke-Aws (@('ecs', 'wait', 'services-inactive', '--cluster', $clusterName, '--services') + $awaited) | Out-Null
    }
}

function Remove-EcsCluster($Inventory) {
    foreach ($cluster in $Inventory.EcsCluster) {
        if (-not (Confirm-Action "ECS cluster $($cluster.Name)" 'Delete')) { continue }
        Write-Host "==> Delete ECS cluster $($cluster.Name)" -ForegroundColor Cyan
        Invoke-Aws @('ecs', 'delete-cluster', '--cluster', $cluster.Name) | Out-Null
    }
}

function Remove-LoadBalancers($Inventory) {
    foreach ($lb in $Inventory.LoadBalancers) {
        if (-not (Confirm-Action "Load balancer $($lb.Name)" 'Delete with its listeners')) { continue }
        Write-Host "==> Delete load balancer $($lb.Name)" -ForegroundColor Cyan
        $listeners = Invoke-Aws @('elbv2', 'describe-listeners', '--load-balancer-arn', $lb.Id)
        foreach ($listener in (ConvertTo-List $listeners.Listeners)) {
            Invoke-Aws @('elbv2', 'delete-listener', '--listener-arn', $listener.ListenerArn) | Out-Null
        }
        Invoke-Aws @('elbv2', 'delete-load-balancer', '--load-balancer-arn', $lb.Id) | Out-Null
        Invoke-Aws @('elbv2', 'wait', 'load-balancers-deleted', '--load-balancer-arns', $lb.Id) | Out-Null
    }
    foreach ($tg in $Inventory.TargetGroups) {
        if (-not (Confirm-Action "Target group $($tg.Name)" 'Delete')) { continue }
        Write-Host "==> Delete target group $($tg.Name)" -ForegroundColor Cyan
        Invoke-Aws @('elbv2', 'delete-target-group', '--target-group-arn', $tg.Id) | Out-Null
    }
}

function Wait-CloudMapOperation([string] $OperationId) {
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $operation = (Invoke-Aws @('servicediscovery', 'get-operation', '--operation-id', $OperationId)).Operation
        if ($operation.Status -eq 'SUCCESS') { return }
        if ($operation.Status -eq 'FAIL') { throw "Cloud Map operation $OperationId failed: $($operation.ErrorMessage)" }
        Start-Sleep -Seconds 5
    }
    throw "Cloud Map operation $OperationId did not finish in 5 minutes."
}

function Remove-Namespaces($Inventory) {
    foreach ($namespace in $Inventory.Namespaces) {
        if (-not (Confirm-Action "Cloud Map namespace $($namespace.Name)" 'Delete with its services')) { continue }
        Write-Host "==> Delete Cloud Map namespace $($namespace.Name)" -ForegroundColor Cyan
        $filter = "Name=NAMESPACE_ID,Values=$($namespace.Id),Condition=EQ"
        $services = Invoke-Aws @('servicediscovery', 'list-services', '--filters', $filter)
        foreach ($service in (ConvertTo-List $services.Services)) {
            $instances = Invoke-Aws @('servicediscovery', 'list-instances', '--service-id', $service.Id)
            foreach ($instance in (ConvertTo-List $instances.Instances)) {
                $deregistration = Invoke-Aws @('servicediscovery', 'deregister-instance', '--service-id', $service.Id, '--instance-id', $instance.Id)
                # Asynchronous: delete-service fails with ResourceInUse until the operation finishes.
                if ($deregistration -and $deregistration.OperationId) { Wait-CloudMapOperation $deregistration.OperationId }
            }
            Invoke-Aws @('servicediscovery', 'delete-service', '--id', $service.Id) | Out-Null
        }
        $operation = Invoke-Aws @('servicediscovery', 'delete-namespace', '--id', $namespace.Id)
        Wait-CloudMapOperation $operation.OperationId
    }
}

function Remove-Roles($Inventory) {
    foreach ($role in $Inventory.Roles) {
        if (-not (Confirm-Action "IAM role $($role.Name)" 'Delete with its policies')) { continue }
        Write-Host "==> Delete IAM role $($role.Name)" -ForegroundColor Cyan
        $inline = Invoke-Aws @('iam', 'list-role-policies', '--role-name', $role.Name)
        foreach ($policyName in (ConvertTo-List $inline.PolicyNames)) {
            Invoke-Aws @('iam', 'delete-role-policy', '--role-name', $role.Name, '--policy-name', $policyName) | Out-Null
        }
        $attached = Invoke-Aws @('iam', 'list-attached-role-policies', '--role-name', $role.Name)
        foreach ($policy in (ConvertTo-List $attached.AttachedPolicies)) {
            Invoke-Aws @('iam', 'detach-role-policy', '--role-name', $role.Name, '--policy-arn', $policy.PolicyArn) | Out-Null
        }
        Invoke-Aws @('iam', 'delete-role', '--role-name', $role.Name) | Out-Null
    }
}

function Remove-Secrets($Inventory) {
    foreach ($secret in $Inventory.Secrets) {
        if (-not (Confirm-Action "Secret $($secret.Name)" 'Delete without recovery')) { continue }
        Write-Host "==> Delete secret $($secret.Name)" -ForegroundColor Cyan
        Invoke-Aws @('secretsmanager', 'delete-secret', '--secret-id', $secret.Id, '--force-delete-without-recovery') | Out-Null
    }
}

function Remove-LogGroups($Inventory) {
    foreach ($group in $Inventory.LogGroups) {
        if (-not (Confirm-Action "Log group $($group.Name)" 'Delete')) { continue }
        Write-Host "==> Delete log group $($group.Name)" -ForegroundColor Cyan
        Invoke-Aws @('logs', 'delete-log-group', '--log-group-name', $group.Name) | Out-Null
    }
}

function Remove-Vpc($Vpc) {
    # Only the VPC verified above (name + tags) and what lives inside it: network interfaces must be
    # gone (ALB and Fargate release theirs a few minutes after deletion), then route tables,
    # subnets, internet gateway and security groups, in dependency order. Anything else (VPC
    # endpoints, NAT gateways) is not ours: the VPC is then left in place and reported.
    $vpcId = $Vpc.Id
    if (-not (Confirm-Action "VPC $vpcId" 'Delete with its subnets, route tables, internet gateway and security groups')) { return }
    Write-Host "==> Delete VPC $vpcId" -ForegroundColor Cyan
    $byVpc = @('--filters', "Name=vpc-id,Values=$vpcId")

    $endpoints = ConvertTo-List (Invoke-Aws (@('ec2', 'describe-vpc-endpoints') + $byVpc)).VpcEndpoints
    $nats = @(ConvertTo-List (Invoke-Aws (@('ec2', 'describe-nat-gateways') + $byVpc)).NatGateways | Where-Object { $_.State -notin 'deleted', 'deleting' })
    if (@($endpoints).Count -gt 0 -or $nats.Count -gt 0) {
        throw "VPC $vpcId has VPC endpoints or NAT gateways that this deployment does not create; delete them manually, then re-run."
    }

    $interfaces = @()
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $interfaces = ConvertTo-List (Invoke-Aws (@('ec2', 'describe-network-interfaces') + $byVpc)).NetworkInterfaces
        if ($interfaces.Count -eq 0) { break }
        Write-Host "    waiting for $($interfaces.Count) network interface(s) to be released..."
        Start-Sleep -Seconds 15
    }
    if ($interfaces.Count -gt 0) {
        throw "VPC $vpcId still has network interfaces ($((($interfaces | ForEach-Object { $_.NetworkInterfaceId }) -join ', '))); wait a few minutes and re-run."
    }

    foreach ($table in (ConvertTo-List (Invoke-Aws (@('ec2', 'describe-route-tables') + $byVpc)).RouteTables)) {
        $associations = ConvertTo-List $table.Associations
        if ($associations | Where-Object { $_.Main }) { continue }
        foreach ($association in $associations) {
            Invoke-Aws @('ec2', 'disassociate-route-table', '--association-id', $association.RouteTableAssociationId) | Out-Null
        }
        Invoke-Aws @('ec2', 'delete-route-table', '--route-table-id', $table.RouteTableId) | Out-Null
    }
    foreach ($subnet in (ConvertTo-List (Invoke-Aws (@('ec2', 'describe-subnets') + $byVpc)).Subnets)) {
        Invoke-Aws @('ec2', 'delete-subnet', '--subnet-id', $subnet.SubnetId) | Out-Null
    }
    $gateways = Invoke-Aws @('ec2', 'describe-internet-gateways', '--filters', "Name=attachment.vpc-id,Values=$vpcId")
    foreach ($gateway in (ConvertTo-List $gateways.InternetGateways)) {
        Invoke-Aws @('ec2', 'detach-internet-gateway', '--internet-gateway-id', $gateway.InternetGatewayId, '--vpc-id', $vpcId) | Out-Null
        Invoke-Aws @('ec2', 'delete-internet-gateway', '--internet-gateway-id', $gateway.InternetGatewayId) | Out-Null
    }
    # Security groups reference each other: drop every rule first, then the groups (not the default one).
    $groups = @(ConvertTo-List (Invoke-Aws (@('ec2', 'describe-security-groups') + $byVpc)).SecurityGroups | Where-Object { $_.GroupName -ne 'default' })
    foreach ($group in $groups) {
        $rules = Invoke-Aws @('ec2', 'describe-security-group-rules', '--filters', "Name=group-id,Values=$($group.GroupId)")
        $ingress = @(ConvertTo-List $rules.SecurityGroupRules | Where-Object { -not $_.IsEgress } | ForEach-Object { $_.SecurityGroupRuleId })
        $egress = @(ConvertTo-List $rules.SecurityGroupRules | Where-Object { $_.IsEgress } | ForEach-Object { $_.SecurityGroupRuleId })
        if ($ingress.Count -gt 0) {
            Invoke-Aws (@('ec2', 'revoke-security-group-ingress', '--group-id', $group.GroupId, '--security-group-rule-ids') + $ingress) | Out-Null
        }
        if ($egress.Count -gt 0) {
            Invoke-Aws (@('ec2', 'revoke-security-group-egress', '--group-id', $group.GroupId, '--security-group-rule-ids') + $egress) | Out-Null
        }
    }
    foreach ($group in $groups) {
        Invoke-Aws @('ec2', 'delete-security-group', '--group-id', $group.GroupId) | Out-Null
    }
    Invoke-Aws @('ec2', 'delete-vpc', '--vpc-id', $vpcId) | Out-Null
}

function Get-TaggedLeftover {
    # Anything still tagged for the deployment, from the resource groups tagging API (CloudFront is
    # global and indexed in us-east-1), plus the IAM roles probed directly.
    # IAM roles are probed by name as well: it is not certain that the tagging API indexes them.
    foreach ($roleKey in @('ExecutionRole', 'TaskRoleApi', 'TaskRoleKeycloak')) {
        $exactName = Get-TeardownResourceName -Environment $environmentName -Resource $roleKey
        $roleResponse = Invoke-Aws -Probe @('iam', 'get-role', '--role-name', $exactName)
        if ($roleResponse) {
            $role = [pscustomobject]@{ Name = $roleResponse.Role.RoleName; Tags = $roleResponse.Role.Tags }
            if (@(Select-OwnedResource -Resources @($role) -Names $exactName -Environment $environmentName).Count -gt 0) {
                $roleResponse.Role.Arn
            }
        }
    }
    $regions = @($script:AwsRegion)
    if ($script:AwsRegion -ne 'us-east-1') { $regions += 'us-east-1' }
    foreach ($queryRegion in $regions) {
        $response = Invoke-Aws -RegionOverride $queryRegion @('resourcegroupstaggingapi', 'get-resources',
            '--tag-filters', 'Key=application,Values=birrapoint', "Key=environment,Values=$environmentName")
        foreach ($mapping in (ConvertTo-List $response.ResourceTagMappingList)) {
            # Deregistered task definitions stay listed (INACTIVE) forever; they cost nothing and
            # never collide (revisions only grow), so they are not a leftover.
            if ($mapping.ResourceARN -notmatch ':task-definition/') { $mapping.ResourceARN }
        }
    }
}

# --- 1. Prerequisites and what exists --------------------------------------------------------

foreach ($tool in 'aws', 'terraform') { Assert-Command $tool }
if (-not $env:NEON_API_KEY) { throw 'NEON_API_KEY is not set (Neon console -> Account settings -> API keys).' }

$varFileRegion = $null
if ($VarFile -and (Test-Path $VarFile)) { $varFileRegion = Get-VarFileRegion -Content (Get-Content -Raw $VarFile) }
$script:AwsRegion = Resolve-AwsRegion -Explicit $Region -EnvRegion $env:AWS_REGION `
    -EnvDefaultRegion $env:AWS_DEFAULT_REGION -VarFileRegion $varFileRegion
if (-not $Region -and $varFileRegion -and $varFileRegion -ne $script:AwsRegion) {
    # Terraform destroys the var file's region; sweeping another one would leave leftovers behind.
    throw "AWS region '$($script:AwsRegion)' (environment) differs from region '$varFileRegion' in the var file; pass -Region to choose. Nothing was changed."
}

$identity = $null
try { $identity = Invoke-Aws @('sts', 'get-caller-identity') } catch { $identity = $null }
if (-not $identity) { throw 'Not signed in to AWS. Run `aws sso login` (or configure credentials) first.' }

# terraform init is read-only for the infrastructure, so it also runs under -WhatIf: the guard
# below needs the workspace state before anything is shown or confirmed.
Invoke-Native 'terraform init' { terraform -chdir="$terraformDir" init -input=false }
$stateList = @(Invoke-Probe { terraform -chdir="$terraformDir" state list })
$stateListExit = $LASTEXITCODE
$stateOutput = ''
$outputExit = 0
if ($stateListExit -eq 0 -and @($stateList | Where-Object { $_ }).Count -gt 0) {
    $stateOutput = (Invoke-Probe { terraform -chdir="$terraformDir" output -raw environment }) -join ''
    $outputExit = $LASTEXITCODE
}
$stateEnvironment = Resolve-StateEnvironment -StateListExitCode $stateListExit -StateList $stateList `
    -OutputExitCode $outputExit -Output $stateOutput
$workspaceLabel = if ($env:TF_WORKSPACE) { $env:TF_WORKSPACE } else { '(default)' }
Write-Host ("HCP workspace {0} holds environment {1}" -f $workspaceLabel, $(if ($stateEnvironment) { $stateEnvironment } else { '(empty state)' }))
if (-not (Test-StateEnvironment -Expected $environmentName -StateEnvironment $stateEnvironment)) {
    throw "HCP workspace $workspaceLabel holds environment '$stateEnvironment', not '$environmentName'. Fix TF_WORKSPACE or -Environment; nothing was changed."
}

# The Neon project of THIS deployment, by id: the account is shared with other deployments, so a
# project is never chosen by name alone. An unreadable output (e.g. the project is already gone from
# the state) just leaves the id empty: then nothing is deleted without -NeonProjectId.
if ($stateEnvironment) {
    $stateNeonOutput = (Invoke-Probe { terraform -chdir="$terraformDir" output -raw neon_project_id }) -join ''
    if ($LASTEXITCODE -eq 0 -and $stateNeonOutput.Trim()) { $script:StateNeonProjectId = $stateNeonOutput.Trim() }
}

$deploymentName = Get-TeardownResourceName -Environment $environmentName -Resource Deployment
$neonProjectName = Get-TeardownResourceName -Environment $environmentName -Resource NeonProject

if (-not $NeonOrgId -and -not $env:TF_VAR_neon_org_id) {
    $NeonOrgId = Resolve-NeonOrgId -Organizations (Get-NeonOrganization)
}
else {
    $NeonOrgId = Resolve-NeonOrgId -Explicit $NeonOrgId -EnvValue $env:TF_VAR_neon_org_id
}
if ($NeonOrgId) { Write-Host "Neon organization $NeonOrgId" }

$neonTarget = Get-NeonDeletionTarget
if ($neonTarget.Action -eq 'Refuse') {
    throw "$($neonTarget.Reason) Nothing was changed."
}

$inventory = Get-AwsInventory

Write-Host ''
Write-Host "AWS account: $($identity.Account) ($($identity.Arn)), region $($script:AwsRegion)"
Write-Host "Resources are selected by exact name AND tags application=birrapoint, environment=$environmentName." -ForegroundColor Yellow
Write-Host 'Will remove EVERYTHING:' -ForegroundColor Yellow
Write-Inventory $inventory
if ($neonTarget.Action -eq 'DeleteById') {
    Write-Host "  - Neon project $neonProjectName ($($neonTarget.ProjectId)) and ALL its data"
}
elseif ($neonTarget.Action -eq 'ReportOnly') {
    Write-Host "  - Neon project: NOT deleted. $($neonTarget.Reason) Found: $((@($neonTarget.Candidates) | ForEach-Object { $_.id }) -join ', ')" -ForegroundColor Yellow
}
else {
    Write-Host "  - Neon project $neonProjectName - none to delete"
}
Write-Host '  - everything Terraform manages in the HCP Terraform workspace (terraform destroy)'
Write-Host '  - local infra/aws/terraform/.terraform'
Write-Host ''

# --- 2. Confirmation -------------------------------------------------------------------------

if (-not $Force -and -not $WhatIfPreference) {
    # The operator types the deployment name; its data is irreversibly lost.
    Read-Confirmation 'This DELETES ALL DATA of the environment listed above, irreversibly.' $deploymentName
}

$script:Problems = @()

# --- 3. terraform destroy ---------------------------------------------------------------------

if ($PSCmdlet.ShouldProcess('infra/aws/terraform', 'terraform destroy (everything)')) {
    try {
        $resolvedVarFile = if ($VarFile) { (Resolve-Path $VarFile).Path } else { $null }
        $destroyArgs = Get-DestroyArgument -TerraformDir $terraformDir -Environment $environmentName `
            -Region $script:AwsRegion -VarFile $resolvedVarFile
        Invoke-Native 'terraform destroy' { terraform @destroyArgs }
    }
    catch {
        # Keep going: the sweep below removes whatever the destroy left behind.
        Write-Warning "terraform destroy did not complete: $($_.Exception.Message)"
        $script:Problems += 'terraform destroy failed; leftovers were swept directly.'
    }
}

# --- 4. Sweep leftovers -------------------------------------------------------------------------

# What the destroy left behind (a fresh listing; under -WhatIf nothing was destroyed, so it is the
# same inventory and each step only prints what it would do).
$leftover = if ($WhatIfPreference) { $inventory } else { Get-AwsInventory }

# CloudFront first: disabling is slow, so it runs while everything else is removed.
Invoke-Step 'Disable CloudFront distributions' { foreach ($d in $leftover.Distributions) { Start-DistributionDisable $d } }
if (@($leftover.EcsCluster).Count -gt 0) {
    Invoke-Step 'Delete ECS services' { Remove-EcsServices $leftover }
    Invoke-Step 'Delete ECS cluster' { Remove-EcsCluster $leftover }
}
Invoke-Step 'Delete load balancer and target groups' { Remove-LoadBalancers $leftover }
Invoke-Step 'Delete Cloud Map namespace' { Remove-Namespaces $leftover }
Invoke-Step 'Delete IAM roles' { Remove-Roles $leftover }
Invoke-Step 'Delete secrets' { Remove-Secrets $leftover }
Invoke-Step 'Delete log groups' { Remove-LogGroups $leftover }
Invoke-Step 'Delete CloudFront distributions' { foreach ($d in $leftover.Distributions) { Remove-Distribution $d } }
Invoke-Step 'Delete CloudFront response headers policy' { foreach ($p in $leftover.Policies) { Remove-ResponseHeadersPolicy $p } }
Invoke-Step 'Delete VPC' { foreach ($v in $leftover.Vpcs) { Remove-Vpc $v } }

# Only the project id of the state (or -NeonProjectId), verified by name, and only if the destroy
# left it behind. Never by name alone.
Invoke-Step 'Delete Neon project' {
    $leftTarget = Get-NeonDeletionTarget
    if ($leftTarget.Action -eq 'Refuse') { throw $leftTarget.Reason }
    if ($leftTarget.Action -eq 'ReportOnly') { Write-Warning $leftTarget.Reason }
    if ($leftTarget.Action -eq 'DeleteById' -and (Confirm-Action "Neon project $($leftTarget.ProjectId)" 'Delete')) {
        Write-Host "==> Delete Neon project $($leftTarget.ProjectId)" -ForegroundColor Cyan
        Invoke-RestMethod -Method Delete -Uri "$neonApi/projects/$([uri]::EscapeDataString($leftTarget.ProjectId))" `
            -Headers $neonHeaders -TimeoutSec 60 | Out-Null
    }
}

$localTerraform = Join-Path $terraformDir '.terraform'
if ((Test-Path $localTerraform) -and $PSCmdlet.ShouldProcess($localTerraform, 'Remove')) {
    Remove-Item -Recurse -Force $localTerraform
}

# --- 5. Verify ------------------------------------------------------------------------------

Write-Host ''
if ($WhatIfPreference) {
    Write-Host 'What-if complete; nothing was changed.' -ForegroundColor Green
    return
}

$remaining = @()
# The tagging index can lag a few minutes behind deletions, so look again before reporting.
for ($attempt = 0; $attempt -lt 4; $attempt++) {
    $remaining = @(Get-TaggedLeftover | Sort-Object -Unique)
    if ($remaining.Count -eq 0) { break }
    if ($attempt -lt 3) { Start-Sleep -Seconds 20 }
}
# Verified by id: a name match alone proves nothing (other deployments share the Neon account).
$neonRemaining = @()
$neonId = if ($script:StateNeonProjectId) { $script:StateNeonProjectId } else { $NeonProjectId }
if ($neonId -and (Get-NeonProjectById $neonId)) { $neonRemaining += "Neon project $neonId" }

foreach ($problem in $script:Problems) { Write-Warning $problem }
$allRemaining = @($remaining) + $neonRemaining
if ($allRemaining.Count -gt 0) {
    Write-Warning "Still present:`n  $($allRemaining -join "`n  ")"
    throw 'Resources are still tagged for the deployment (the tagging index can lag a few minutes). Re-run ./infra/aws/teardown.ps1 to retry.'
}
if ($script:Problems.Count -gt 0) {
    throw 'Nothing is tagged for the deployment any more, but some steps reported errors (see warnings above).'
}

Write-Host 'Environment removed. The next deploy starts from scratch.' -ForegroundColor Green
