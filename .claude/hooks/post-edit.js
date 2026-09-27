#!/usr/bin/env node
// PostToolUse: auto-format the edited file. Must stay fast (it runs after every edit): backend C#
// gets a whitespace-only format (no MSBuild load, ~2 s), frontend sources get the pinned local
// Prettier (<1 s). ESLint and type-checking run once per turn in stop-check.js, not per edit.
// Files outside backend/ and frontend/ (docs, memory, scratchpad) are left untouched.
const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const root = process.env.CLAUDE_PROJECT_DIR || path.resolve(__dirname, '..', '..');
const frontend = path.join(root, 'frontend');
// Invoke the JS entry point with node directly: the Windows .cmd shims add seconds per call.
const prettier = path.join(frontend, 'node_modules', 'prettier', 'bin', 'prettier.cjs');

let input = '';
process.stdin.on('data', d => (input += d));
process.stdin.on('end', () => {
  let file = '';
  try { file = (JSON.parse(input).tool_input || {}).file_path || ''; } catch { process.exit(0); }
  if (!file || !fs.existsSync(file)) process.exit(0);
  file = path.resolve(file);
  const rel = path.relative(root, file);
  if (rel.startsWith('..') || path.isAbsolute(rel)) process.exit(0); // outside the repo

  const run = (cmd, cwd) => { try { execSync(cmd, { cwd, stdio: 'pipe', timeout: 45000 }); return null; }
    catch (e) { return ((e.stdout || '') + '\n' + (e.stderr || '')).toString().trim(); } };
  const posix = p => p.split(path.sep).join('/');

  if (file.endsWith('.cs') && posix(rel).startsWith('backend/')) {
    // Style/analyzer rules are enforced by `dotnet format --verify-no-changes` in the gates.
    run(`dotnet format whitespace backend --folder --include "${posix(path.relative(path.join(root, 'backend'), file))}"`, root);
  } else if (/\.(ts|html|scss|css|json)$/.test(file) && posix(rel).startsWith('frontend/') && fs.existsSync(prettier)) {
    run(`node "${prettier}" --write "${path.relative(frontend, file)}"`, frontend);
  }
  process.exit(0);
});
