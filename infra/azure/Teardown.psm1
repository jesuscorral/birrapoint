# Pure helpers behind infra/azure/teardown.ps1 (T140, T143): the confirmation check, resource names,
# which Neon project may be deleted, and the `terraform destroy` arguments. Kept free of
# Azure/Neon calls so they are unit-testable (infra/azure/tests/Teardown.Tests.ps1). Windows PowerShell
# 5.1 and PowerShell 7.

$ErrorActionPreference = 'Stop'

# Resource acronyms; must match the locals in infra/azure/terraform/main.tf (ADR-0021).
$script:ResourceAcronyms = @{
    ResourceGroup = 'rg'
    KeyVault      = 'kv'
    NeonProject   = 'neon'
}

function Test-TeardownConfirmation {
    # The operator must type the exact expected word (the resource group name); anything else - including
    # "yes" or a different casing - aborts.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Expected,
        [AllowNull()] [AllowEmptyString()] [string] $Answer
    )
    if ($null -eq $Answer) { return $false }
    $Answer.Trim() -ceq $Expected
}

function ConvertTo-EnvironmentName {
    # Same rule as Terraform's `environment` variable: 2-10 letters/digits starting with a letter
    # (the Key Vault name birrapoint-<environment>-kv is limited to 24 characters), lower-cased.
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)] [AllowEmptyString()] [string] $Environment)
    if ($Environment -notmatch '^[A-Za-z][A-Za-z0-9]{1,9}$') {
        throw "Invalid environment '$Environment': 2-10 letters or digits, starting with a letter (e.g. PROD)."
    }
    $Environment.ToLowerInvariant()
}

function Get-TeardownResourceName {
    # birrapoint-<environment>-<acronym> of the resources the sweep looks for.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $Environment,
        [Parameter(Mandatory = $true)] [ValidateSet('ResourceGroup', 'KeyVault', 'NeonProject')] [string] $Resource
    )
    "birrapoint-$(ConvertTo-EnvironmentName $Environment)-$($script:ResourceAcronyms[$Resource])"
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
    # The committed, non-secret inputs of an environment, shared with deploy-azure.yml (ADR-0022).
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

function Resolve-NeonDeletionTarget {
    # Which Neon project the teardown may delete. The Neon account is shared by the Azure and AWS
    # deployments, so a project is NEVER chosen by name alone:
    #   - the id comes from the Terraform state (-StateProjectId) or, when the state is empty, from
    #     an explicit -ExplicitProjectId given by the operator;
    #   - that project must exist in -ProjectsFound and carry exactly -ExpectedName (this cloud's
    #     name); another name is refused, which is what keeps one cloud's teardown away from the
    #     other cloud's database;
    #   - with no id at all, name matches are only reported (Action ReportOnly), never deleted.
    # Actions: DeleteById, Refuse, ReportOnly, None.
    [CmdletBinding()]
    param(
        [string] $StateProjectId,
        [string] $ExplicitProjectId,
        [AllowEmptyCollection()] [object[]] $ProjectsFound = @(),
        [Parameter(Mandatory = $true)] [string] $ExpectedName
    )
    $projects = @($ProjectsFound | Where-Object { $null -ne $_ })
    $result = { param($action, $id, $reason, $candidates)
        [pscustomobject]@{ Action = $action; ProjectId = $id; Reason = $reason; Candidates = @($candidates) } }

    $state = if ([string]::IsNullOrWhiteSpace($StateProjectId)) { $null } else { $StateProjectId.Trim() }
    $explicit = if ([string]::IsNullOrWhiteSpace($ExplicitProjectId)) { $null } else { $ExplicitProjectId.Trim() }
    if ($state -and $explicit -and $state -cne $explicit) {
        return & $result 'Refuse' $null "The Terraform state holds Neon project '$state' but -NeonProjectId is '$explicit'." @()
    }
    $targetId = if ($state) { $state } else { $explicit }

    if (-not $targetId) {
        $named = @($projects | Where-Object { $_.name -ceq $ExpectedName })
        if ($named.Count -gt 0) {
            return & $result 'ReportOnly' $null "No Neon project id in the Terraform state; '$ExpectedName' matches by name only. Pass -NeonProjectId <id> to delete it." $named
        }
        return & $result 'None' $null 'No Neon project id in the state and none matches by name.' @()
    }

    $project = @($projects | Where-Object { $_.id -ceq $targetId }) | Select-Object -First 1
    if (-not $project) {
        return & $result 'None' $null "Neon project '$targetId' does not exist (already deleted)." @()
    }
    if ($project.name -cne $ExpectedName) {
        return & $result 'Refuse' $null "Neon project '$targetId' is named '$($project.name)', not '$ExpectedName'; refusing to delete another deployment's database." @($project)
    }
    & $result 'DeleteById' $targetId "Neon project '$targetId' ($ExpectedName)." @($project)
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
    # so the -var flags win. The image namespace is mandatory but meaningless for a destroy, so it
    # gets a placeholder, as do the required SMTP variables when there is no var file. The
    # subscription comes from ARM_SUBSCRIPTION_ID / the az login; the location only when given.
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $TerraformDir,
        [Parameter(Mandatory = $true)] [string] $Environment,
        [string] $Location,
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
    if ($Location) {
        $arguments += "-var=location=$Location"
    }
    if (-not $VarFile) {
        $arguments += '-var=smtp_host=unused.invalid', '-var=smtp_from_address=unused@unused.invalid'
    }
    $arguments
}

Export-ModuleMember -Function Test-TeardownConfirmation, ConvertTo-EnvironmentName, Get-TeardownResourceName,
    Select-NeonProjectToDelete, Resolve-NeonDeletionTarget, Get-DestroyArgument,
    Test-StateEnvironment, Resolve-StateEnvironment, Get-EnvironmentVarFile, Resolve-NeonOrgId
