#!/usr/bin/env node
// PreToolUse guard: blocks destructive bash commands before they run.
let input = '';
process.stdin.on('data', d => (input += d));
process.stdin.on('end', () => {
  let cmd = '';
  try { cmd = (JSON.parse(input).tool_input || {}).command || ''; } catch { process.exit(0); }
  const rules = [
    [/rm\s+(-[a-z]*r[a-z]*f|-[a-z]*f[a-z]*r)\s+[\/~]/i, 'Blocked: recursive force delete on root/home path.'],
    [/git\s+push\s+.*--force(?!-with-lease)/i, 'Blocked: force push. Use --force-with-lease on a feature branch if truly needed.'],
    [/git\s+reset\s+--hard/i, 'Blocked: hard reset. Use git stash or restore specific files.'],
    [/dotnet\s+ef\s+database\s+drop/i, 'Blocked: database drop. Ask the user to run this manually.'],
    [/drop\s+(database|table)\s/i, 'Blocked: destructive SQL. Ask the user to run this manually.'],
    [/git\s+clean\s+-[a-z]*f/i, 'Blocked: git clean -f deletes untracked files. Ask the user first.']
  ];
  for (const [re, msg] of rules) {
    if (re.test(cmd)) { console.error(msg); process.exit(2); }
  }
  process.exit(0);
});
