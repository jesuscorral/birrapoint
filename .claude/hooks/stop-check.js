#!/usr/bin/env node
// Stop hook: quality gate loop. Blocks Claude from finishing while build/lint/unit-tests fail.
// Deliberately excludes Testcontainers integration tests and Playwright E2E (too slow to run on
// every stop) — those are the test-runner agent's job.
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
  if (fs.existsSync('backend/BirraPoint.sln')) {
    const buildOut = run('dotnet build backend/BirraPoint.sln -v q --nologo');
    if (buildOut) failures.push('dotnet build FAILED:\n' + buildOut);
    else if (fs.existsSync('backend/tests/BirraPoint.Api.UnitTests')) {
      const testOut = run('dotnet test backend/tests/BirraPoint.Api.UnitTests -v q --nologo');
      if (testOut) failures.push('dotnet test (unit) FAILED:\n' + testOut);
    }
  }
  if (fs.existsSync('frontend/package.json')) {
    const lintOut = run('npm run lint --prefix frontend --silent');
    if (lintOut) failures.push('Angular lint FAILED:\n' + lintOut);
    const jestOut = run('npm test --prefix frontend --silent -- --ci');
    if (jestOut) failures.push('Jest FAILED:\n' + jestOut);
  }
  if (failures.length) { console.error('Quality gate failed — fix before finishing:\n\n' + failures.join('\n\n')); process.exit(2); }
  process.exit(0);
});
