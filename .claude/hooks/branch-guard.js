#!/usr/bin/env node
// PreToolUse guard: blocks edits to implementation/test code while on main, per CLAUDE.md's
// mandatory per-task branch workflow ("feature/<task-id>" off main, step 1).
const { execSync } = require('child_process');
let input = '';
process.stdin.on('data', d => (input += d));
process.stdin.on('end', () => {
  let file = '';
  try {
    const parsed = JSON.parse(input);
    file = (parsed.tool_input || {}).file_path || '';
  } catch { process.exit(0); }
  if (!file) process.exit(0);

  const isGuardedPath = /(^|[\\/])backend[\\/](src|tests)[\\/]|(^|[\\/])frontend[\\/](src|e2e)[\\/]/i.test(file);
  if (!isGuardedPath) process.exit(0);

  let branch = '';
  try { branch = execSync('git rev-parse --abbrev-ref HEAD', { stdio: 'pipe' }).toString().trim(); }
  catch { process.exit(0); } // not a git repo / detached — don't block

  if (branch === 'main' || branch === 'master') {
    console.error(
      `Blocked: editing ${file} directly on '${branch}'. ` +
      `CLAUDE.md's Implementation workflow step 1 requires a per-task branch first: ` +
      `git checkout -b feature/<task-id>`
    );
    process.exit(2);
  }
  process.exit(0);
});
