#!/usr/bin/env node
// Stop hook: quality gate. Blocks Claude from finishing while changed code doesn't build, lint or
// type-check (backend: dotnet build; frontend: ESLint on changed files + tsc).
// Only checks stacks with uncommitted changes, and skips a check whose inputs already passed, so
// docs-only or repeated turns cost nothing.
const { execSync } = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');

const root = process.env.CLAUDE_PROJECT_DIR || path.resolve(__dirname, '..', '..');
const cacheFile = path.join(os.tmpdir(), `birrapoint-stop-check-${crypto.createHash('md5').update(root).digest('hex')}.json`);

let input = '';
process.stdin.on('data', d => (input += d));
process.stdin.on('end', () => {
  let data = {};
  try { data = JSON.parse(input); } catch {}
  if (data.stop_hook_active) process.exit(0); // prevent infinite loop

  const sh = (cmd, cwd = root, timeout = 150000) => execSync(cmd, { cwd, stdio: 'pipe', timeout }).toString();
  const run = (cmd, cwd) => { try { sh(cmd, cwd); return null; }
    catch (e) { return ((e.stdout || '') + '\n' + (e.stderr || '')).toString().slice(-2000); } };

  let changed = [];
  try {
    changed = sh('git status --porcelain --untracked-files=all', root, 15000)
      .split('\n').filter(Boolean).map(l => l.slice(3).trim().replace(/^"|"$/g, ''));
  } catch { process.exit(0); }

  // Fingerprint = changed paths + their current content, per stack.
  const fingerprint = files => {
    const h = crypto.createHash('sha1');
    for (const f of files.sort()) {
      h.update(f);
      try { h.update(fs.readFileSync(path.join(root, f))); } catch {}
    }
    return h.digest('hex');
  };
  let cache = {};
  try { cache = JSON.parse(fs.readFileSync(cacheFile, 'utf8')); } catch {}

  const frontendSources = changed.filter(f => /^frontend\/src\/.*\.(ts|html)$/.test(f) && fs.existsSync(path.join(root, f)));
  const checks = [
    {
      name: 'backend',
      files: changed.filter(f => /^backend\/.*\.(cs|csproj|props|json)$/.test(f)),
      cmd: 'dotnet build backend/BirraPoint.sln -v q --nologo',
      cwd: root,
      label: 'dotnet build FAILED'
    },
    {
      name: 'frontend-lint',
      files: frontendSources,
      // Only the changed files, with a persistent cache; node + JS entry avoids the slow .cmd shim.
      cmd: `node node_modules/eslint/bin/eslint.js --cache --cache-location node_modules/.cache/eslint-hook/ ${frontendSources.map(f => `"${f.slice('frontend/'.length)}"`).join(' ')}`,
      cwd: path.join(root, 'frontend'),
      label: 'ESLint FAILED'
    },
    {
      name: 'frontend-tsc',
      files: frontendSources.filter(f => f.endsWith('.ts') && !f.endsWith('.spec.ts')),
      cmd: 'node node_modules/typescript/bin/tsc --noEmit -p tsconfig.app.json',
      cwd: path.join(root, 'frontend'),
      label: 'Frontend type-check (tsc) FAILED'
    }
  ];

  const failures = [];
  for (const c of checks) {
    if (!c.files.length) continue;
    const fp = fingerprint(c.files);
    if (cache[c.name] === fp) continue; // same inputs already passed
    const out = run(c.cmd, c.cwd);
    if (out) failures.push(`${c.label}:\n${out}`);
    else cache[c.name] = fp;
  }
  try { fs.writeFileSync(cacheFile, JSON.stringify(cache)); } catch {}

  if (failures.length) {
    console.error('Quality gate failed — fix before finishing:\n\n' + failures.join('\n\n'));
    process.exit(2);
  }
  process.exit(0);
});
