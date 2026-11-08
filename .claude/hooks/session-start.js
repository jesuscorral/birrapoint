#!/usr/bin/env node
// SessionStart: prints the current branch, the active spec folder and the open GitHub issues, so a
// session never reports work from a stale branch or a stale memory. Stdout becomes session context.
// Never blocks: any failure (offline, no remote, no gh) just shortens the output.
const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const root = process.env.CLAUDE_PROJECT_DIR || process.cwd();
// Never open a credential prompt (Git Credential Manager GUI) at session start.
const env = { ...process.env, GIT_TERMINAL_PROMPT: '0', GCM_INTERACTIVE: 'never' };
const run = (cmd, timeout = 5000) => {
  try { return execSync(cmd, { cwd: root, stdio: ['ignore', 'pipe', 'ignore'], timeout, env }).toString().trim(); }
  catch { return ''; }
};
const git = (args, timeout) => run(`git ${args}`, timeout);

git('fetch -q origin main', 10000);
const branch = git('rev-parse --abbrev-ref HEAD');
const behind = git('rev-list --count HEAD..origin/main');
const dirty = git('status --porcelain').split('\n').filter(Boolean).length;

const lines = [`Branch: ${branch || 'unknown'}${behind && behind !== '0' ? ` (${behind} commits behind origin/main)` : ''}${dirty ? `, ${dirty} uncommitted file(s)` : ''}.`];
if (/^(main|master)$/.test(branch)) lines.push('On main: create feature/<issue>-<slug> (or chore/<topic>) before editing; commits and pushes on main are blocked.');

try {
  const dir = JSON.parse(fs.readFileSync(path.join(root, '.specify', 'feature.json'), 'utf8')).feature_directory;
  if (dir) lines.push(`Active spec folder (.specify/feature.json): ${dir}`);
} catch {}

let issues = [];
try { issues = JSON.parse(run('gh issue list --state open --limit 15 --json number,title', 8000) || '[]'); } catch {}
if (issues.length) lines.push('Open issues (start one with /deliver <n>):', ...issues.map(i => `- #${i.number} ${i.title.slice(0, 100)}`));

console.log(lines.join('\n'));
