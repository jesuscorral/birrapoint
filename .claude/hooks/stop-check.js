#!/usr/bin/env node
// Stop hook: quality gate loop. Blocks Claude from finishing while build/lint fail.
const { execSync } = require('child_process');
const fs = require('fs');
let input = '';
process.stdin.on('data', d => (input += d));
process.stdin.on('end', () => {
  let data = {};
  try { data = JSON.parse(input); } catch {}
  if (data.stop_hook_active) process.exit(0); // prevent infinite loop
  const failures = [];
  const run = (cmd, cwd) => { try { execSync(cmd, { stdio: 'pipe', timeout: 150000, cwd }); return null; }
    catch (e) { return ((e.stdout || '') + '\n' + (e.stderr || '')).toString().slice(-2000); } };
  if (fs.existsSync('Birrapoint.sln')) {
    const out = run('dotnet build Birrapoint.sln -v q --nologo');
    if (out) failures.push('dotnet build FAILED:\n' + out);
  }
  if (fs.existsSync('src/birrapoint-web/package.json')) {
    const out = run('npm run lint --prefix src/birrapoint-web --silent');
    if (out) failures.push('Angular lint FAILED:\n' + out);
  }
  if (failures.length) { console.error('Quality gate failed — fix before finishing:\n\n' + failures.join('\n\n')); process.exit(2); }
  process.exit(0);
});
