#!/usr/bin/env node
// PostToolUse: auto-format edited file, feed lint errors back to Claude (exit 2).
const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

// The repo has no root node_modules — `npx <tool>` from repo root can't see a workspace's local
// devDependency and falls back to whatever's in the global npx cache (which may be an
// incompatible major version). Walk up from the edited file to find that workspace's own
// node_modules/.bin binary instead, so formatting/linting always runs the pinned local version.
// Also return the workspace root (two levels above the .bin binary) so the tool can be run with
// cwd set there — ESLint's flat-config discovery searches from cwd, not from the target file.
function findLocalBin(startDir, binName) {
  const exe = process.platform === 'win32' ? `${binName}.cmd` : binName;
  let dir = startDir;
  for (let i = 0; i < 6; i++) {
    const candidate = path.join(dir, 'node_modules', '.bin', exe);
    if (fs.existsSync(candidate)) return { bin: candidate, cwd: dir };
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  return null;
}

let input = '';
process.stdin.on('data', d => (input += d));
process.stdin.on('end', () => {
  let file = '';
  try { file = (JSON.parse(input).tool_input || {}).file_path || ''; } catch { process.exit(0); }
  if (!file || !fs.existsSync(file)) process.exit(0);
  const run = (cmd, cwd) => { try { execSync(cmd, { stdio: 'pipe', timeout: 45000, cwd }); return null; }
    catch (e) { return ((e.stdout || '') + '\n' + (e.stderr || '')).toString().trim(); } };
  if (file.endsWith('.cs')) {
    run(`dotnet format --include "${file}" --no-restore`); // best-effort format
  } else if (/\.(ts|html|scss|css|json)$/.test(file)) {
    const dir = path.dirname(file);
    const prettier = findLocalBin(dir, 'prettier');
    run(prettier ? `"${prettier.bin}" --write "${file}"` : `npx prettier --write "${file}"`, prettier?.cwd);
    if (file.endsWith('.ts') && !file.endsWith('.spec.ts')) {
      const eslint = findLocalBin(dir, 'eslint');
      const lintErr = run(eslint ? `"${eslint.bin}" "${file}"` : `npx eslint "${file}"`, eslint?.cwd);
      if (lintErr) { console.error(`ESLint issues in ${file}:\n${lintErr}\nFix these before continuing.`); process.exit(2); }
    }
  }
  process.exit(0);
});
