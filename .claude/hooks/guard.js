#!/usr/bin/env node
// PreToolUse guard: blocks destructive or workflow-breaking shell commands before they run.
// Rules are matched against each command segment (split on newlines, ;, &&, ||, |), anchored at
// the segment start, so text that merely mentions a command (commit messages, heredocs, echo) is
// not blocked. Strings passed to `bash -c` / `pwsh -Command` are checked as commands too.
// Exit 2 = blocked, the message is shown to Claude. Tests: node .claude/hooks/guard.test.js
const { execSync } = require('child_process');

let input = '';
process.stdin.on('data', d => (input += d));
process.stdin.on('end', () => {
  let data = {};
  try { data = JSON.parse(input); } catch { process.exit(0); }
  const cmd = (data.tool_input || {}).command || '';
  const cwd = data.cwd || process.env.CLAUDE_PROJECT_DIR || process.cwd();

  // Current branch, looked up only when a branch rule needs it. GUARD_CURRENT_BRANCH is for tests.
  let branch;
  const currentBranch = () => {
    if (branch !== undefined) return branch;
    if (process.env.GUARD_CURRENT_BRANCH !== undefined) return (branch = process.env.GUARD_CURRENT_BRANCH);
    try {
      branch = execSync('git rev-parse --abbrev-ref HEAD', { cwd, stdio: ['ignore', 'pipe', 'ignore'], timeout: 5000 })
        .toString().trim();
    } catch { branch = ''; }
    return branch;
  };
  const onMain = () => /^(main|master)$/.test(currentBranch());

  // aws, optionally .exe, then any global flags (with an optional value) before the service.
  const aws = String.raw`^aws(\.exe)?(\s+--?[\w-]+(\s+(?!-)\S+)?)*`;
  const readers = String.raw`^(cat|type|less|more|head|tail|bat|nl|gc|get-content|select-string|grep|egrep|fgrep|rg|sed|awk|base64|xxd|od|strings|cp|copy|copy-item|tar|zip)\b`;
  // .env files (not .env.example), Terraform state/secret vars, app secrets, cloud credentials.
  const secrets = String.raw`((^|[\s/\\=])\.env(\.(?!example\b)[\w.-]+)?(?=\s|$)|\.tfstate\b|(^|[\s/\\])terraform\.tfvars\b|(^|[\s/\\])secrets\.json\b|appsettings\.Production\.json|\.aws[/\\]credentials|\.azure[/\\]|\.pfx\b)`;

  const rules = [
    [/^rm\s+(-[a-z]*r[a-z]*f|-[a-z]*f[a-z]*r)\s+["']?[\/~]/i, 'Blocked: recursive force delete on root/home path.'],
    [/^git\s+push\b.*(--force(?!-with-lease)|\s-f\b)/i, 'Blocked: force push. Use --force-with-lease on a feature branch only if truly needed.'],
    [/^git\s+push\s+\S+\s+(\S+:)?(main|master)\b/i, 'Blocked: direct push to main. Push a feature branch and open a PR.'],
    [/^git\s+.*--no-verify\b/i, 'Blocked: skipping git hooks (--no-verify) is not allowed in this repo.'],
    [/^git\s+commit\b.*\s-[a-zA-Z]*n[a-zA-Z]*(\s|$)/, 'Blocked: `git commit -n` skips git hooks (same as --no-verify).'],
    [/^git\s+reset\s+--hard/i, 'Blocked: hard reset. Use git stash or restore specific files.'],
    [/^git\s+clean\s+-[a-z]*f/i, 'Blocked: git clean -f deletes untracked files. Ask the user first.'],
    [/^gh\s+pr\s+merge\b/i, 'Blocked: PRs are merged by a human (CLAUDE.md workflow step 5).'],
    [/^gh\s+pr\s+review\b.*--(approve|request-changes)\b|^gh\s+pr\s+review\b.*\s-[ar]\b/i, 'Blocked: never approve or request changes; post findings as a plain comment (`gh pr comment`).'],
    [/^gh\s+api\b.*\/pulls\/\d+\/(merge|reviews)\b/i, 'Blocked: merging or reviewing PRs through the API is for humans only.'],
    [/^dotnet\s+ef\s+database\s+drop/i, 'Blocked: database drop. Ask the user to run this manually.'],
    [/^(docker\s+exec\s.*)?psql\b.*\bdrop\s+(database|table|schema)\s/i, 'Blocked: destructive SQL. Ask the user to run this manually.'],
    [/^terraform\b(\s+-chdir=\S+)?\s+(apply|destroy)\b/i, 'Blocked: terraform apply/destroy changes real cloud resources. Ask the user to run it (or use terraform plan).'],
    [/^((pwsh|powershell)(\.exe)?\s+(-\S+\s+)*)?\S*teardown\.ps1\b(?!.*-WhatIf(?!:\$?false))/i, 'Blocked: teardown.ps1 deletes the whole cloud environment (Azure or AWS). Run with -WhatIf or ask the user.'],
    [/^((pwsh|powershell)(\.exe)?\s+(-\S+\s+)*)?\S*teardown\.ps1\b.*-WhatIf:\$?false/i, 'Blocked: teardown.ps1 with -WhatIf switched off deletes the whole cloud environment. Ask the user.'],
    [/^az\s+(group|keyvault|containerapp)\s+delete\b/i, 'Blocked: deleting Azure resources. Ask the user to run it manually.'],
    [new RegExp(aws + String.raw`\s+[\w-]+\s+(batch-)?(delete|terminate|remove|deregister|detach|revoke|purge)-`, 'i'), 'Blocked: deleting or detaching AWS resources. Ask the user to run it manually.'],
    [new RegExp(aws + String.raw`\s+s3\s+(rm|rb)\b`, 'i'), 'Blocked: deleting S3 objects or buckets. Ask the user to run it manually.'],
    [new RegExp(aws + String.raw`\s+s3\s+sync\b.*\s--delete\b`, 'i'), 'Blocked: aws s3 sync --delete removes objects. Ask the user to run it manually.'],
    [/^docker\s+(volume\s+(rm|prune)|system\s+prune)/i, 'Blocked: removing Docker volumes wipes local Postgres/Keycloak data. Ask the user first.'],
    [new RegExp(readers + '.*' + secrets, 'i'), 'Blocked: reading secrets (.env, tfstate, terraform.tfvars, secrets.json, production settings, cloud credentials). Ask the user if a value is really needed.'],
    [/^git\s+commit\b/i, 'Blocked: committing on main. Create a feature/<task-id> (or chore/<topic>) branch first.', onMain],
    [/^git\s+push\b/i, 'Blocked: this push would update main. Push a feature branch and open a PR.', seg => onMain() && pushesCurrentBranch(seg)]
  ];

  // `git push` with no refspec (or with HEAD) pushes the current branch.
  function pushesCurrentBranch(seg) {
    const positional = seg.split(/\s+/).slice(2).filter(t => !t.startsWith('-'));
    return positional.length <= 1 || positional.slice(1).includes('HEAD');
  }

  function check(command, depth) {
    if (depth > 3) return null;
    // Strings run by a nested shell are commands in their own right.
    const nested = /(?:^|[\s;&|(])(?:bash|sh|zsh|pwsh|powershell)(?:\.exe)?\s+(?:-\S+\s+)*?-(?:c|command)\s+("(?:[^"\\]|\\.)*"|'[^']*')/gi;
    for (const m of command.matchAll(nested)) {
      const msg = check(m[1].slice(1, -1).replace(/\\(["\\])/g, '$1'), depth + 1);
      if (msg) return msg;
    }
    // Quoted strings are blanked, so their content (commit messages, PR bodies, echo text) never
    // matches a rule. Leading wrappers are stripped repeatedly: sudo / time / env and NAME=value
    // assignments (AWS_PROFILE=x aws ...), so a prefix never hides the command.
    const unquoted = command.replace(/"(?:[^"\\]|\\.)*"|'[^']*'/g, '""');
    const segments = unquoted.split(/\r?\n|;|&&|\|\||\|/)
      .map(s => s.trim().replace(/^((sudo|time|env)\s+|[A-Za-z_][A-Za-z0-9_]*=\S*\s+)+/, ''));
    for (const seg of segments) {
      for (const [re, msg, when] of rules) {
        if (re.test(seg) && (!when || when(seg))) return msg;
      }
    }
    return null;
  }

  const msg = check(cmd, 0);
  if (msg) { console.error(msg); process.exit(2); }
  process.exit(0);
});
