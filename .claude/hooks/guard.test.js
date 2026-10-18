#!/usr/bin/env node
// Tests for guard.js. Run: node .claude/hooks/guard.test.js
// Each sample is fed to the hook as the PreToolUse JSON; exit code 2 means blocked, 0 allowed.
const { spawnSync } = require('node:child_process');
const path = require('node:path');

const guard = path.join(__dirname, 'guard.js');

const blocked = [
  // AWS deletions, with and without prefixes / global flags / .exe
  'aws ec2 delete-vpc --vpc-id vpc-1',
  'AWS_PROFILE=prod aws ec2 delete-vpc --vpc-id vpc-1',
  'AWS_PROFILE=prod AWS_REGION=eu-west-1 aws ec2 delete-vpc --vpc-id vpc-1',
  'env AWS_PROFILE=prod aws ec2 delete-vpc --vpc-id vpc-1',
  'aws.exe ec2 delete-vpc --vpc-id vpc-1',
  'aws --region eu-west-1 --profile prod ecs delete-service --service x',
  'aws ecr batch-delete-image --repository-name x --image-ids imageTag=1',
  'aws ec2 terminate-instances --instance-ids i-1',
  'aws iam detach-role-policy --role-name r --policy-arn arn:p',
  'aws ec2 revoke-security-group-ingress --group-id sg-1',
  'aws iam remove-role-from-instance-profile --role-name r',
  'aws servicediscovery deregister-instance --service-id s --instance-id i',
  'aws secretsmanager delete-secret --secret-id x --force-delete-without-recovery',
  'aws cloudfront delete-distribution --id E1',
  'aws s3 rm s3://bucket --recursive',
  'aws s3 rb s3://bucket --force',
  'aws s3 sync ./dist s3://bucket --delete',
  'echo ok && aws ec2 delete-vpc --vpc-id vpc-1',
  // teardown without -WhatIf, or with it switched off
  './infra/aws/teardown.ps1',
  'pwsh ./infra/azure/teardown.ps1 -Force',
  'pwsh -NoProfile -File ./infra/aws/teardown.ps1',
  'FOO=1 ./infra/aws/teardown.ps1',
  './infra/aws/teardown.ps1 -WhatIf:$false',
  './infra/aws/teardown.ps1 -WhatIf:false',
  './infra/aws/teardown.ps1 -WhatIf:$false -WhatIf',
  // existing rules still hold
  'terraform apply',
  'AWS_PROFILE=x terraform destroy',
  'terraform -chdir=infra/aws/terraform destroy',
  'az group delete --name rg',
  'git push --force origin feature/x',
  'git reset --hard',
  // PRs are merged and approved by a human only
  'gh pr merge 12 --squash',
  'gh pr merge',
  'gh pr review 12 --approve',
  'gh pr review 12 --request-changes -b x',
  'gh api -X PUT repos/o/r/pulls/12/merge',
  // short-flag hook bypass
  'git commit -n -m x',
  'git commit -nm x',
  // commands hidden inside a shell -c string
  'bash -c "terraform apply"',
  "sh -c 'git push --force origin x'",
  'pwsh -NoProfile -Command "terraform destroy"',
  'powershell -c "./infra/aws/teardown.ps1"',
  // secrets read through the shell (Read() deny rules don't cover Bash)
  'cat .env',
  'cat backend/.env.local',
  'type .env',
  'Get-Content infra/azure/terraform/terraform.tfvars',
  'head -5 infra/aws/terraform/terraform.tfstate',
  'grep -i password backend/src/BirraPoint.Api/appsettings.Production.json',
  'cat ~/.aws/credentials',
];

// Blocked only while the current branch is main.
const blockedOnMain = [
  'git push',
  'git push -u origin HEAD',
  'git commit -m x',
];

const allowed = [
  'aws ec2 describe-vpcs',
  'AWS_PROFILE=prod aws ec2 describe-vpcs',
  'aws sso login',
  'aws sts get-caller-identity',
  'aws iam list-roles',
  'aws iam get-role --role-name r',
  'aws ecs describe-services --cluster c --services s',
  'aws s3 ls',
  'aws s3 sync ./dist s3://bucket',
  'aws s3 cp a s3://bucket/a',
  'aws configure list',
  './infra/aws/teardown.ps1 -WhatIf',
  'pwsh ./infra/azure/teardown.ps1 -WhatIf',
  './infra/aws/teardown.ps1 -WhatIf:$true',
  'AWS_PROFILE=prod ./infra/aws/teardown.ps1 -WhatIf',
  'git commit -m "aws ec2 delete-vpc and teardown.ps1"',
  'echo aws ec2 delete-vpc',
  'terraform plan',
  'terraform fmt -check -recursive',
  'git push origin feature/T146',
  'Invoke-Pester infra/aws/tests',
  'gh pr view 12',
  'gh pr create --base main --title "x" --body-file b.md',
  'gh pr comment 12 --body-file f.md',
  'gh pr checks 12',
  'git commit -m "never gh pr merge or cat .env"',
  'git commit --amend -m x',
  'cat .env.example',
  'cat frontend/src/environments/environment.ts',
  'cat infra/aws/terraform/environments/prod.tfvars',
  'bash -c "dotnet build backend/BirraPoint.sln"',
  'bash .github/scripts/version.test.sh',
  // on a feature branch these are fine
  'git push',
  'git push -u origin HEAD',
  'git commit -m x',
];

let failures = 0;
// GUARD_CURRENT_BRANCH overrides the git lookup so branch rules are testable.
function exitCodeFor(command, branch = 'feature/test') {
  const result = spawnSync(process.execPath, [guard], {
    input: JSON.stringify({ tool_input: { command } }),
    encoding: 'utf8',
    env: { ...process.env, GUARD_CURRENT_BRANCH: branch },
  });
  return result.status;
}
for (const command of blocked) {
  const code = exitCodeFor(command);
  if (code === 2) console.log(`ok   - blocked: ${command}`);
  else { console.log(`FAIL - should be blocked (exit ${code}): ${command}`); failures++; }
}
for (const command of blockedOnMain) {
  const code = exitCodeFor(command, 'main');
  if (code === 2) console.log(`ok   - blocked on main: ${command}`);
  else { console.log(`FAIL - should be blocked on main (exit ${code}): ${command}`); failures++; }
}
for (const command of allowed) {
  const code = exitCodeFor(command);
  if (code === 0) console.log(`ok   - allowed: ${command}`);
  else { console.log(`FAIL - should be allowed (exit ${code}): ${command}`); failures++; }
}
console.log(failures === 0 ? '\nAll guard tests passed.' : `\n${failures} guard test(s) failed.`);
process.exit(failures === 0 ? 0 : 1);
