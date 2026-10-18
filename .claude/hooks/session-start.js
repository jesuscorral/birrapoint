#!/usr/bin/env node
// SessionStart: prints the current branch and the pending tasks from origin/main's tasks.md, so a
// session never reports work from a stale branch or a stale memory. Stdout becomes session context.
// Never blocks: any failure (offline, no remote) just shortens the output.
const { execSync } = require('child_process');

const root = process.env.CLAUDE_PROJECT_DIR || process.cwd();
// Never open a credential prompt (Git Credential Manager GUI) at session start.
const env = { ...process.env, GIT_TERMINAL_PROMPT: '0', GCM_INTERACTIVE: 'never' };
const git = (args, timeout = 5000) => {
  try { return execSync(`git ${args}`, { cwd: root, stdio: ['ignore', 'pipe', 'ignore'], timeout, env }).toString().trim(); }
  catch { return ''; }
};

git('fetch -q origin main', 10000);
const branch = git('rev-parse --abbrev-ref HEAD');
const behind = git('rev-list --count HEAD..origin/main');
const dirty = git('status --porcelain').split('\n').filter(Boolean).length;

const lines = [`Branch: ${branch || 'unknown'}${behind && behind !== '0' ? ` (${behind} commits behind origin/main)` : ''}${dirty ? `, ${dirty} uncommitted file(s)` : ''}.`];
if (/^(main|master)$/.test(branch)) lines.push('On main: create feature/<task-id> (or chore/<topic>) before editing; commits and pushes on main are blocked.');

const tasks = git('show origin/main:specs/001-birrapoint-mvp/tasks.md');
const pending = tasks.split('\n')
  .map(l => l.match(/^- \[ \] (T\d+[A-Z]?)\b\s*(?:\[[^\]]*\]\s*)*(.*)$/))
  .filter(Boolean)
  .map(m => `${m[1]} ${m[2].replace(/\*\*/g, '').slice(0, 90)}`);
if (pending.length) lines.push(`Pending tasks (origin/main tasks.md): ${pending.length}`, ...pending.map(p => `- ${p}`));

console.log(lines.join('\n'));
