# Pester 5+ tests for infra/aws/Teardown.psm1 - the pure decisions behind infra/aws/teardown.ps1 (T146).
# Run: Invoke-Pester infra/aws/tests
# (Windows PowerShell 5.1 ships Pester 3.4: Install-Module Pester -MinimumVersion 5.0 -Scope CurrentUser)

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../Teardown.psm1') -Force

    # AWS CLI shapes: [{Key,Value}] (most services), [{key,value}] (ECS) and a plain map (logs).
    function New-Tag([string] $Application = 'birrapoint', [string] $Environment = 'prod') {
        @(
            [pscustomobject]@{ Key = 'application'; Value = $Application },
            [pscustomobject]@{ Key = 'environment'; Value = $Environment },
            [pscustomobject]@{ Key = 'managed-by'; Value = 'terraform' }
        )
    }
}

Describe 'Test-TeardownConfirmation' {
    It 'accepts the exact expected word' {
        Test-TeardownConfirmation -Expected 'birrapoint-prod' -Answer 'birrapoint-prod' | Should -BeTrue
    }

    It 'tolerates surrounding whitespace' {
        Test-TeardownConfirmation -Expected 'birrapoint-prod' -Answer '  birrapoint-prod ' | Should -BeTrue
    }

    It 'rejects <answer>' -ForEach @(
        @{ answer = '' }, @{ answer = 'y' }, @{ answer = 'yes' }, @{ answer = 'BirraPoint-Prod' }, @{ answer = 'birrapoint-prod2' }, @{ answer = 'birrapoint-dev' }
    ) {
        Test-TeardownConfirmation -Expected 'birrapoint-prod' -Answer $answer | Should -BeFalse
    }

    It 'rejects a null answer (non-interactive prompt)' {
        Test-TeardownConfirmation -Expected 'birrapoint-prod' -Answer $null | Should -BeFalse
    }
}

Describe 'ConvertTo-EnvironmentName' {
    It 'lower-cases a valid environment' {
        ConvertTo-EnvironmentName 'PROD' | Should -Be 'prod'
    }

    It 'rejects <value>' -ForEach @(
        @{ value = '' }, @{ value = 'a' }, @{ value = '1prod' }, @{ value = 'prod-eu' }, @{ value = 'abcdefghijk' }
    ) {
        { ConvertTo-EnvironmentName $value } | Should -Throw
    }
}

Describe 'Get-TeardownResourceName' {
    It 'matches the names in infra/aws/terraform (ADR-0021): <resource>' -ForEach @(
        @{ resource = 'Deployment'; expected = 'birrapoint-prod' },
        @{ resource = 'Vpc'; expected = 'birrapoint-prod-vpc' },
        @{ resource = 'Alb'; expected = 'birrapoint-prod-alb' },
        @{ resource = 'TargetGroupWeb'; expected = 'birrapoint-prod-web-tg' },
        @{ resource = 'TargetGroupKeycloak'; expected = 'birrapoint-prod-kc-tg' },
        @{ resource = 'EcsCluster'; expected = 'birrapoint-prod-ecs' },
        @{ resource = 'ServiceApi'; expected = 'birrapoint-prod-api' },
        @{ resource = 'ServiceWeb'; expected = 'birrapoint-prod-web' },
        @{ resource = 'ServiceKeycloak'; expected = 'birrapoint-prod-kc' },
        @{ resource = 'CloudMapNamespace'; expected = 'birrapoint-prod.local' },
        @{ resource = 'ExecutionRole'; expected = 'birrapoint-prod-ecs-exec' },
        @{ resource = 'TaskRoleApi'; expected = 'birrapoint-prod-api-task' },
        @{ resource = 'TaskRoleKeycloak'; expected = 'birrapoint-prod-kc-task' },
        @{ resource = 'DistributionWeb'; expected = 'birrapoint-prod-web' },
        @{ resource = 'DistributionKeycloak'; expected = 'birrapoint-prod-kc' },
        @{ resource = 'ResponseHeadersPolicy'; expected = 'birrapoint-prod-security' },
        @{ resource = 'LogGroupPrefix'; expected = '/birrapoint/prod/' },
        @{ resource = 'DockerHubSecret'; expected = 'birrapoint-prod-dockerhub' },
        @{ resource = 'NeonProject'; expected = 'birrapoint-prod-aws-neon' }
    ) {
        Get-TeardownResourceName -Environment 'PROD' -Resource $resource | Should -Be $expected
    }

    It 'lower-cases the environment' {
        Get-TeardownResourceName -Environment 'Dev' -Resource Alb | Should -Be 'birrapoint-dev-alb'
    }

    It 'rejects an invalid environment' {
        { Get-TeardownResourceName -Environment 'x' -Resource Alb } | Should -Throw
    }
}

Describe 'Get-TeardownSecretName' {
    It 'lists the generic application secret names plus the environment Docker Hub secret' {
        $names = Get-TeardownSecretName -Environment 'PROD'
        $names | Should -Contain 'ConnectionStrings--db'
        $names | Should -Contain 'ConnectionStrings--dbDirect'
        $names | Should -Contain 'Keycloak--AdminClientSecret'
        $names | Should -Contain 'keycloak-db-password'
        $names | Should -Contain 'keycloak-bootstrap-admin-password'
        $names | Should -Contain 'Smtp--Password'
        $names | Should -Contain 'birrapoint-prod-dockerhub'
    }
}

Describe 'Get-TeardownTag' {
    It 'is application=birrapoint plus the lower-cased environment' {
        $tags = Get-TeardownTag -Environment 'PROD'
        $tags['application'] | Should -Be 'birrapoint'
        $tags['environment'] | Should -Be 'prod'
    }
}

Describe 'ConvertTo-TagMap' {
    It 'reads [{Key,Value}] (most AWS services)' {
        $map = ConvertTo-TagMap -Tags @([pscustomobject]@{ Key = 'a'; Value = '1' })
        $map['a'] | Should -Be '1'
    }

    It 'reads [{key,value}] (ECS)' {
        $map = ConvertTo-TagMap -Tags @([pscustomobject]@{ key = 'a'; value = '1' })
        $map['a'] | Should -Be '1'
    }

    It 'reads a plain map (CloudWatch Logs)' {
        $map = ConvertTo-TagMap -Tags ([pscustomobject]@{ a = '1'; b = '2' })
        $map['a'] | Should -Be '1'
        $map['b'] | Should -Be '2'
    }

    It 'reads a hashtable' {
        (ConvertTo-TagMap -Tags @{ a = '1' })['a'] | Should -Be '1'
    }

    It 'is empty for no tags' {
        (ConvertTo-TagMap -Tags $null).Count | Should -Be 0
        (ConvertTo-TagMap -Tags @()).Count | Should -Be 0
    }

    It 'compares keys case-sensitively, as AWS does' {
        $map = ConvertTo-TagMap -Tags @([pscustomobject]@{ Key = 'Application'; Value = 'birrapoint' })
        $map.ContainsKey('application') | Should -BeFalse
    }
}

Describe 'Select-OwnedResource' {
    BeforeAll {
        $resources = @(
            [pscustomobject]@{ Name = 'birrapoint-prod-alb'; Id = 'owned'; Tags = (New-Tag) },
            [pscustomobject]@{ Name = 'birrapoint-prod-alb'; Id = 'other-env'; Tags = (New-Tag -Environment 'dev') },
            [pscustomobject]@{ Name = 'birrapoint-prod-alb'; Id = 'other-app'; Tags = (New-Tag -Application 'other') },
            [pscustomobject]@{ Name = 'birrapoint-prod-alb'; Id = 'untagged'; Tags = @() },
            [pscustomobject]@{ Name = 'birrapoint-prod-alb'; Id = 'null-tags'; Tags = $null },
            [pscustomobject]@{ Name = 'birrapoint-prod-alb-2'; Id = 'prefix-name'; Tags = (New-Tag) },
            [pscustomobject]@{ Name = 'xbirrapoint-prod-alb'; Id = 'suffix-name'; Tags = (New-Tag) },
            [pscustomobject]@{ Name = 'BIRRAPOINT-PROD-ALB'; Id = 'other-case'; Tags = (New-Tag) }
        )
    }

    It 'selects only the exact name with both deployment tags' {
        $selected = @(Select-OwnedResource -Resources $resources -Names 'birrapoint-prod-alb' -Environment 'PROD')
        $selected.Count | Should -Be 1
        $selected[0].Id | Should -Be 'owned'
    }

    It 'never selects another environment' {
        # Only what is tagged environment=dev can be selected for dev - never the prod-tagged resource.
        $selected = @(Select-OwnedResource -Resources $resources -Names 'birrapoint-prod-alb' -Environment 'dev')
        ($selected | Where-Object { $_.Id -eq 'owned' }) | Should -BeNullOrEmpty
        $selected.Count | Should -Be 1
        $selected[0].Id | Should -Be 'other-env'
        @(Select-OwnedResource -Resources $resources -Names 'birrapoint-prod-alb' -Environment 'staging').Count | Should -Be 0
    }

    It 'never selects another application, even with the right name and environment' {
        $selected = @(Select-OwnedResource -Resources $resources -Names 'birrapoint-prod-alb' -Environment 'prod')
        ($selected | Where-Object { $_.Id -eq 'other-app' }) | Should -BeNullOrEmpty
    }

    It 'never selects untagged resources' {
        $selected = @(Select-OwnedResource -Resources $resources -Names 'birrapoint-prod-alb' -Environment 'prod')
        ($selected | Where-Object { $_.Id -in 'untagged', 'null-tags' }) | Should -BeNullOrEmpty
    }

    It 'does not match prefixes, suffixes or other casing of a name' {
        $selected = @(Select-OwnedResource -Resources $resources -Names 'birrapoint-prod-alb' -Environment 'prod')
        ($selected | Where-Object { $_.Id -in 'prefix-name', 'suffix-name', 'other-case' }) | Should -BeNullOrEmpty
    }

    It 'accepts several exact names' {
        $three = @(
            [pscustomobject]@{ Name = 'a'; Tags = (New-Tag) },
            [pscustomobject]@{ Name = 'b'; Tags = (New-Tag) },
            [pscustomobject]@{ Name = 'c'; Tags = (New-Tag) }
        )
        @(Select-OwnedResource -Resources $three -Names 'a', 'c' -Environment 'prod').Count | Should -Be 2
    }

    It 'matches by exact prefix when asked (log groups)' {
        $groups = @(
            [pscustomobject]@{ Name = '/birrapoint/prod/api'; Tags = (New-Tag) },
            [pscustomobject]@{ Name = '/birrapoint/prod2/api'; Tags = (New-Tag) },
            [pscustomobject]@{ Name = '/birrapoint/prod/api'; Tags = (New-Tag -Environment 'dev') },
            [pscustomobject]@{ Name = '/birrapoint/dev/api'; Tags = (New-Tag) },
            [pscustomobject]@{ Name = '/other/prod/api'; Tags = (New-Tag) }
        )
        $selected = @(Select-OwnedResource -Resources $groups -NamePrefix '/birrapoint/prod/' -Environment 'prod')
        $selected.Count | Should -Be 1
        $selected[0].Name | Should -Be '/birrapoint/prod/api'
    }

    It 'reads the name from the Name tag when the resource has no Name property (VPC)' {
        $vpcs = @(
            [pscustomobject]@{ VpcId = 'vpc-1'; Tags = @((New-Tag) + [pscustomobject]@{ Key = 'Name'; Value = 'birrapoint-prod-vpc' }) },
            [pscustomobject]@{ VpcId = 'vpc-2'; Tags = @((New-Tag) + [pscustomobject]@{ Key = 'Name'; Value = 'default' }) }
        )
        $selected = @(Select-OwnedResource -Resources $vpcs -Names 'birrapoint-prod-vpc' -Environment 'prod')
        $selected.Count | Should -Be 1
        $selected[0].VpcId | Should -Be 'vpc-1'
    }

    It 'handles lower-case ECS tag arrays' {
        $services = @([pscustomobject]@{ Name = 'birrapoint-prod-api'; Tags = @(
                    [pscustomobject]@{ key = 'application'; value = 'birrapoint' },
                    [pscustomobject]@{ key = 'environment'; value = 'prod' }) })
        @(Select-OwnedResource -Resources $services -Names 'birrapoint-prod-api' -Environment 'prod').Count | Should -Be 1
    }

    It 'refuses to run without a name or prefix, which would select everything' {
        { Select-OwnedResource -Resources $resources -Environment 'prod' } | Should -Throw '*name*'
        { Select-OwnedResource -Resources $resources -Names @() -Environment 'prod' } | Should -Throw '*name*'
    }

    It 'handles an empty list' {
        @(Select-OwnedResource -Resources @() -Names 'x' -Environment 'prod').Count | Should -Be 0
    }

    It 'skips the tag check only for non-taggable types, and only with -IgnoreTags' {
        $policies = @(
            [pscustomobject]@{ Name = 'birrapoint-prod-security'; Id = 'p-1' },
            [pscustomobject]@{ Name = 'birrapoint-prod-securityx'; Id = 'p-2' }
        )
        @(Select-OwnedResource -Resources $policies -Names 'birrapoint-prod-security' -Environment 'prod').Count | Should -Be 0
        $selected = @(Select-OwnedResource -Resources $policies -Names 'birrapoint-prod-security' -Environment 'prod' -IgnoreTags)
        $selected.Count | Should -Be 1
        $selected[0].Id | Should -Be 'p-1'
    }
}

Describe 'Select-OwnedSecret' {
    BeforeAll {
        $secrets = @(
            [pscustomobject]@{ Name = 'ConnectionStrings--db'; Id = 'owned-db'; Tags = (New-Tag) },
            [pscustomobject]@{ Name = 'ConnectionStrings--db'; Id = 'dev-db'; Tags = (New-Tag -Environment 'dev') },
            [pscustomobject]@{ Name = 'ConnectionStrings--db'; Id = 'other-app-db'; Tags = (New-Tag -Application 'someapp') },
            [pscustomobject]@{ Name = 'ConnectionStrings--db'; Id = 'untagged-db'; Tags = $null },
            [pscustomobject]@{ Name = 'Smtp--Password'; Id = 'owned-smtp'; Tags = (New-Tag) },
            [pscustomobject]@{ Name = 'birrapoint-prod-dockerhub'; Id = 'owned-dockerhub'; Tags = (New-Tag) },
            [pscustomobject]@{ Name = 'birrapoint-dev-dockerhub'; Id = 'dev-dockerhub'; Tags = (New-Tag -Environment 'dev') },
            [pscustomobject]@{ Name = 'unrelated'; Id = 'unrelated'; Tags = (New-Tag) },
            [pscustomobject]@{ Name = 'prod/db'; Id = 'foreign'; Tags = $null }
        )
    }

    It 'selects only the deployment secrets carrying both tags' {
        $ids = @(Select-OwnedSecret -Secrets $secrets -Environment 'PROD' | ForEach-Object { $_.Id }) | Sort-Object
        $ids | Should -Be @('owned-db', 'owned-dockerhub', 'owned-smtp')
    }

    It 'refuses generically named secrets of another environment, another app or without tags' {
        $ids = @(Select-OwnedSecret -Secrets $secrets -Environment 'prod' | ForEach-Object { $_.Id })
        $ids | Should -Not -Contain 'dev-db'
        $ids | Should -Not -Contain 'other-app-db'
        $ids | Should -Not -Contain 'untagged-db'
        $ids | Should -Not -Contain 'dev-dockerhub'
        $ids | Should -Not -Contain 'unrelated'
        $ids | Should -Not -Contain 'foreign'
    }

    It 'selects the dev secrets for dev only' {
        $ids = @(Select-OwnedSecret -Secrets $secrets -Environment 'dev' | ForEach-Object { $_.Id }) | Sort-Object
        $ids | Should -Be @('dev-db', 'dev-dockerhub')
    }
}

Describe 'Test-AwsNotFoundError' {
    It 'recognizes <message>' -ForEach @(
        @{ message = 'An error occurred (NoSuchEntity) when calling the GetRole operation: The role with name x cannot be found.' },
        @{ message = 'An error occurred (LoadBalancerNotFound) when calling the DescribeLoadBalancers operation' },
        @{ message = 'An error occurred (TargetGroupNotFound) when calling the DescribeTargetGroups operation' },
        @{ message = 'An error occurred (ResourceNotFoundException) when calling the DescribeSecret operation' },
        @{ message = 'An error occurred (NoSuchDistribution) when calling the GetDistribution operation' },
        @{ message = 'An error occurred (ClusterNotFoundException) when calling the DescribeClusters operation' },
        @{ message = 'An error occurred (InvalidVpcID.NotFound) when calling the DescribeVpcs operation' }
    ) {
        Test-AwsNotFoundError -Message $message | Should -BeTrue
    }

    It 'does not hide real failures: <message>' -ForEach @(
        @{ message = 'An error occurred (AccessDenied) when calling the GetRole operation' },
        @{ message = 'An error occurred (ThrottlingException) when calling the DescribeServices operation' },
        @{ message = 'Unable to locate credentials. You can configure credentials by running "aws configure".' },
        @{ message = '' }
    ) {
        Test-AwsNotFoundError -Message $message | Should -BeFalse
    }
}

Describe 'Resolve-AwsRegion' {
    It 'prefers -Region, then AWS_REGION, then AWS_DEFAULT_REGION, then the var file' {
        Resolve-AwsRegion -Explicit 'us-east-1' -EnvRegion 'eu-west-1' -EnvDefaultRegion 'eu-west-2' -VarFileRegion 'eu-central-1' | Should -Be 'us-east-1'
        Resolve-AwsRegion -EnvRegion 'eu-west-1' -EnvDefaultRegion 'eu-west-2' -VarFileRegion 'eu-central-1' | Should -Be 'eu-west-1'
        Resolve-AwsRegion -EnvDefaultRegion 'eu-west-2' -VarFileRegion 'eu-central-1' | Should -Be 'eu-west-2'
        Resolve-AwsRegion -VarFileRegion ' eu-central-1 ' | Should -Be 'eu-central-1'
    }

    It 'falls back to the Terraform variable default when nothing is set' {
        Resolve-AwsRegion | Should -Be 'eu-central-1'
    }
}

Describe 'Get-VarFileRegion' {
    It 'reads region = "..." from tfvars text' {
        Get-VarFileRegion -Content "environment = `"PROD`"`nregion      = `"eu-west-3`"`n" | Should -Be 'eu-west-3'
    }

    It 'ignores comments and other variables that end in region' {
        Get-VarFileRegion -Content "# region = `"us-east-1`"`nneon_region = `"aws-eu-central-1`"`n" | Should -BeNullOrEmpty
    }

    It 'is empty for no content' {
        Get-VarFileRegion -Content '' | Should -BeNullOrEmpty
        Get-VarFileRegion -Content $null | Should -BeNullOrEmpty
    }
}

Describe 'Get-DestroyArgument' {
    BeforeAll {
        $common = @{ TerraformDir = 'C:/repo/infra/aws/terraform'; Environment = 'prod' }
    }

    It 'always destroys everything: never a -target' {
        $arguments = Get-DestroyArgument @common -VarFile 'C:/repo/infra/aws/terraform/terraform.tfvars'

        ($arguments | Where-Object { $_ -like '-target*' }) | Should -BeNullOrEmpty
        $arguments[0..1] | Should -Be @('-chdir=C:/repo/infra/aws/terraform', 'destroy')
        $arguments | Should -Contain '-input=false'
        $arguments | Should -Contain '-auto-approve'
    }

    It 'passes the environment so the destroy resolves the same resource names' {
        Get-DestroyArgument @common -VarFile 'x.tfvars' | Should -Contain '-var=environment=prod'
    }

    It 'passes an explicit region even with a var file' {
        (Get-DestroyArgument @common -VarFile 'x.tfvars' | Where-Object { $_ -like '-var=region=*' }) | Should -BeNullOrEmpty
        Get-DestroyArgument @common -VarFile 'x.tfvars' -Region 'eu-west-3' | Should -Contain '-var=region=eu-west-3'
    }

    It 'always passes the region when there is no var file, so the provider targets the swept region' {
        Get-DestroyArgument @common -Region 'eu-west-3' | Should -Contain '-var=region=eu-west-3'
    }

    It 'puts the var file before the -var flags' {
        $arguments = @(Get-DestroyArgument @common -VarFile 'x.tfvars')
        $varFileIndex = [array]::IndexOf($arguments, '-var-file=x.tfvars')
        $firstVarIndex = [array]::IndexOf($arguments, '-var=environment=prod')

        $varFileIndex | Should -BeGreaterThan -1
        $varFileIndex | Should -BeLessThan $firstVarIndex
    }

    It 'passes placeholders for the mandatory image namespace and release version' {
        $arguments = Get-DestroyArgument @common -VarFile 'x.tfvars'

        ($arguments | Where-Object { $_ -like '-var=image_namespace=*' }).Count | Should -Be 1
        ($arguments | Where-Object { $_ -eq '-var=release_version=latest' }).Count | Should -Be 1
    }

    It 'uses the var file when there is one' {
        $arguments = Get-DestroyArgument @common -VarFile 'C:/repo/infra/aws/terraform/terraform.tfvars'

        $arguments | Should -Contain '-var-file=C:/repo/infra/aws/terraform/terraform.tfvars'
        ($arguments | Where-Object { $_ -like '-var=smtp_*' }) | Should -BeNullOrEmpty
    }

    It 'supplies placeholders for the required SMTP variables when there is no var file' {
        $arguments = Get-DestroyArgument @common -Region 'eu-central-1'

        ($arguments | Where-Object { $_ -like '-var-file=*' }) | Should -BeNullOrEmpty
        ($arguments | Where-Object { $_ -like '-var=smtp_host=*' }).Count | Should -Be 1
        ($arguments | Where-Object { $_ -like '-var=smtp_from_address=*' }).Count | Should -Be 1
    }
}

Describe 'Select-NeonProjectToDelete' {
    BeforeAll {
        $projects = @(
            [pscustomobject]@{ id = 'p-1'; name = 'birrapoint-prod-neon' },
            [pscustomobject]@{ id = 'p-2'; name = 'birrapoint-prod-neon-old' },
            [pscustomobject]@{ id = 'p-3'; name = 'my-birrapoint-prod-neon' },
            [pscustomobject]@{ id = 'p-4'; name = 'other' }
        )
    }

    It 'selects only exact name matches' {
        $selected = @(Select-NeonProjectToDelete -Projects $projects -Name 'birrapoint-prod-neon')
        $selected.Count | Should -Be 1
        $selected[0].id | Should -Be 'p-1'
    }

    It 'selects nothing when no project has the exact name' {
        @(Select-NeonProjectToDelete -Projects $projects -Name 'birra').Count | Should -Be 0
    }

    It 'handles an empty project list' {
        @(Select-NeonProjectToDelete -Projects @() -Name 'x').Count | Should -Be 0
    }
}

Describe 'Resolve-NeonDeletionTarget' {
    BeforeAll {
        $expected = 'birrapoint-prod-aws-neon'
        $other = 'birrapoint-prod-neon'
        $mine = [pscustomobject]@{ id = 'p-mine'; name = $expected }
        $dup = [pscustomobject]@{ id = 'p-dup'; name = $expected }
        $foreign = [pscustomobject]@{ id = 'p-foreign'; name = $other }
    }

    It 'deletes the state project id when its name matches' {
        $target = Resolve-NeonDeletionTarget -StateProjectId 'p-mine' -ProjectsFound @($mine, $dup) -ExpectedName $expected
        $target.Action | Should -Be 'DeleteById'
        $target.ProjectId | Should -Be 'p-mine'
    }

    It 'refuses when the state project id carries another name' {
        $target = Resolve-NeonDeletionTarget -StateProjectId 'p-foreign' -ProjectsFound @($foreign, $mine) -ExpectedName $expected
        $target.Action | Should -Be 'Refuse'
        $target.ProjectId | Should -BeNullOrEmpty
    }

    It 'has nothing to delete when the state project id no longer exists' {
        $target = Resolve-NeonDeletionTarget -StateProjectId 'p-gone' -ProjectsFound @($dup) -ExpectedName $expected
        $target.Action | Should -Be 'None'
    }

    It 'only reports a name match when the state is empty and no id is given' {
        $target = Resolve-NeonDeletionTarget -ProjectsFound @($mine) -ExpectedName $expected
        $target.Action | Should -Be 'ReportOnly'
        $target.ProjectId | Should -BeNullOrEmpty
        @($target.Candidates).id | Should -Be @('p-mine')
    }

    It 'has nothing to do when the state is empty and nothing matches' {
        (Resolve-NeonDeletionTarget -ProjectsFound @($foreign) -ExpectedName $expected).Action | Should -Be 'None'
        (Resolve-NeonDeletionTarget -ProjectsFound @() -ExpectedName $expected).Action | Should -Be 'None'
    }

    It 'deletes an explicit id whose name matches' {
        $target = Resolve-NeonDeletionTarget -ExplicitProjectId 'p-mine' -ProjectsFound @($mine, $dup) -ExpectedName $expected
        $target.Action | Should -Be 'DeleteById'
        $target.ProjectId | Should -Be 'p-mine'
    }

    It 'refuses an explicit id with another name' {
        $target = Resolve-NeonDeletionTarget -ExplicitProjectId 'p-foreign' -ProjectsFound @($foreign, $mine) -ExpectedName $expected
        $target.Action | Should -Be 'Refuse'
    }

    It 'refuses when the explicit id and the state id differ' {
        $target = Resolve-NeonDeletionTarget -StateProjectId 'p-mine' -ExplicitProjectId 'p-dup' -ProjectsFound @($mine, $dup) -ExpectedName $expected
        $target.Action | Should -Be 'Refuse'
    }

    It 'never selects a project named for the other cloud, whatever the id source' {
        foreach ($parameters in @{ StateProjectId = 'p-foreign' }, @{ ExplicitProjectId = 'p-foreign' }) {
            $target = Resolve-NeonDeletionTarget @parameters -ProjectsFound @($foreign) -ExpectedName $expected
            $target.Action | Should -Not -Be 'DeleteById'
            $target.ProjectId | Should -BeNullOrEmpty
        }
        (Resolve-NeonDeletionTarget -ProjectsFound @($foreign) -ExpectedName $expected).Action | Should -Not -Be 'DeleteById'
    }

    It 'matches names case-sensitively and exactly' {
        $near = [pscustomobject]@{ id = 'p-near'; name = $expected.ToUpperInvariant() }
        (Resolve-NeonDeletionTarget -StateProjectId 'p-near' -ProjectsFound @($near) -ExpectedName $expected).Action | Should -Be 'Refuse'
    }
}

Describe 'Test-StateEnvironment' {
    It 'accepts an empty state (nothing deployed yet)' -ForEach @(
        @{ state = $null }, @{ state = '' }, @{ state = '  ' }
    ) {
        Test-StateEnvironment -Expected 'PROD' -StateEnvironment $state | Should -BeTrue
    }

    It 'accepts the same environment regardless of casing' {
        Test-StateEnvironment -Expected 'PROD' -StateEnvironment 'prod' | Should -BeTrue
        Test-StateEnvironment -Expected 'prod' -StateEnvironment ' PROD ' | Should -BeTrue
    }

    It 'rejects a workspace that holds another environment' {
        Test-StateEnvironment -Expected 'dev' -StateEnvironment 'prod' | Should -BeFalse
    }

    It 'does not accept a prefix of the environment' {
        Test-StateEnvironment -Expected 'prod' -StateEnvironment 'prod2' | Should -BeFalse
    }
}

Describe 'Get-EnvironmentVarFile' {
    It 'is environments/ENV.tfvars (lower-cased) under the Terraform directory' {
        $file = Get-EnvironmentVarFile -TerraformDir 'repo/infra/aws/terraform' -Environment 'PROD'
        ($file -replace '\\', '/') | Should -Be 'repo/infra/aws/terraform/environments/prod.tfvars'
    }
}

Describe 'Resolve-StateEnvironment' {
    It 'aborts when the state cannot be listed' {
        { Resolve-StateEnvironment -StateListExitCode 1 -StateList @() -OutputExitCode 0 -Output 'prod' } | Should -Throw '*reading*state*'
    }

    It 'treats a successful, empty state list as nothing deployed' -ForEach @(
        @{ list = @() }, @{ list = @('') }, @{ list = $null }
    ) {
        Resolve-StateEnvironment -StateListExitCode 0 -StateList $list -OutputExitCode 1 -Output '' | Should -Be ''
    }

    It 'returns the environment output of a non-empty state, trimmed' {
        Resolve-StateEnvironment -StateListExitCode 0 -StateList @('aws_vpc.main') -OutputExitCode 0 -Output " PROD`n" | Should -Be 'PROD'
    }

    It 'aborts when a non-empty state has no readable environment output' {
        { Resolve-StateEnvironment -StateListExitCode 0 -StateList @('a.b') -OutputExitCode 1 -Output '' } | Should -Throw '*environment*'
    }

    It 'aborts when a non-empty state has an empty environment output' {
        { Resolve-StateEnvironment -StateListExitCode 0 -StateList @('a.b') -OutputExitCode 0 -Output '  ' } | Should -Throw '*environment*'
    }
}

Describe 'Resolve-NeonOrgId' {
    It 'prefers the explicit -NeonOrgId' {
        Resolve-NeonOrgId -Explicit 'org-a' -EnvValue 'org-b' -Organizations @([pscustomobject]@{ id = 'org-c' }) | Should -Be 'org-a'
    }

    It 'falls back to TF_VAR_neon_org_id, the variable Terraform uses' {
        Resolve-NeonOrgId -Explicit '' -EnvValue ' org-b ' -Organizations @() | Should -Be 'org-b'
    }

    It 'uses the only organization of the API key when nothing is set' {
        Resolve-NeonOrgId -Organizations @([pscustomobject]@{ id = 'org-c'; name = 'mine' }) | Should -Be 'org-c'
    }

    It 'returns nothing for a key without organizations (personal account)' {
        Resolve-NeonOrgId -Organizations @() | Should -BeNullOrEmpty
    }

    It 'refuses to guess between several organizations' {
        $orgs = @([pscustomobject]@{ id = 'org-1' }, [pscustomobject]@{ id = 'org-2' })
        { Resolve-NeonOrgId -Organizations $orgs } | Should -Throw '*NeonOrgId*'
    }
}
