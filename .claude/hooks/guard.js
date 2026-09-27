#!/usr/bin/env node
// PreToolUse guard: blocks destructive or workflow-breaking bash commands before they run.
// Rules are matched against each command segment (split on newlines, ;, &&, ||, |), anchored at
// the segment start, so text that merely mentions a command (commit messages, heredocs, echo) is
// not blocked. Exit 2 = blocked, the message is shown to Claude.
let input = '';
process.stdin.on('data', d => (input += d));
process.stdin.on('end', () => {
  let cmd = '';
  try { cmd = (JSON.parse(input).tool_input || {}).command || ''; } catch { process.exit(0); }

  // Quoted strings are blanked first, so their content (commit messages, PR bodies, echo text)
  // never matches a rule.
  const unquoted = cmd.replace(/"(?:[^"\\]|\\.)*"|'[^']*'/g, '""');
  const segments = unquoted.split(/\r?\n|;|&&|\|\||\|/).map(s => s.trim().replace(/^(sudo|time|env\s+\S+=\S+)\s+/, ''));
  const rules = [
    [/^rm\s+(-[a-z]*r[a-z]*f|-[a-z]*f[a-z]*r)\s+["']?[\/~]/i, 'Blocked: recursive force delete on root/home path.'],
    [/^git\s+push\b.*(--force(?!-with-lease)|\s-f\b)/i, 'Blocked: force push. Use --force-with-lease on a feature branch only if truly needed.'],
    [/^git\s+push\s+\S+\s+(\S+:)?(main|master)\b/i, 'Blocked: direct push to main. Push a feature branch and open a PR.'],
    [/^git\s+.*--no-verify\b/i, 'Blocked: skipping git hooks (--no-verify) is not allowed in this repo.'],
    [/^git\s+reset\s+--hard/i, 'Blocked: hard reset. Use git stash or restore specific files.'],
    [/^git\s+clean\s+-[a-z]*f/i, 'Blocked: git clean -f deletes untracked files. Ask the user first.'],
    [/^dotnet\s+ef\s+database\s+drop/i, 'Blocked: database drop. Ask the user to run this manually.'],
    [/^(docker\s+exec\s.*)?psql\b.*\bdrop\s+(database|table|schema)\s/i, 'Blocked: destructive SQL. Ask the user to run this manually.'],
    [/^terraform\b(\s+-chdir=\S+)?\s+(apply|destroy)\b/i, 'Blocked: terraform apply/destroy changes real cloud resources. Ask the user to run it (or use deploy.ps1 -WhatIf).'],
    [/^((pwsh|powershell)(\.exe)?\s+(-\S+\s+)*)?\S*teardown\.ps1\b(?!.*-WhatIf)/i, 'Blocked: teardown.ps1 deletes the Azure environment. Run with -WhatIf or ask the user.'],
    [/^az\s+(group|keyvault|containerapp)\s+delete\b/i, 'Blocked: deleting Azure resources. Ask the user to run it manually.'],
    [/^docker\s+(volume\s+(rm|prune)|system\s+prune)/i, 'Blocked: removing Docker volumes wipes local Postgres/Keycloak data. Ask the user first.']
  ];
  for (const seg of segments) {
    for (const [re, msg] of rules) {
      if (re.test(seg)) { console.error(msg); process.exit(2); }
    }
  }
  process.exit(0);
});
