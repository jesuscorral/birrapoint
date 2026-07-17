#!/usr/bin/env node
// PostToolUse: auto-format edited file, feed lint errors back to Claude (exit 2).
const { execSync } = require('child_process');
const fs = require('fs');
let input = '';
process.stdin.on('data', d => (input += d));
process.stdin.on('end', () => {
  let file = '';
  try { file = (JSON.parse(input).tool_input || {}).file_path || ''; } catch { process.exit(0); }
  if (!file || !fs.existsSync(file)) process.exit(0);
  const run = (cmd) => { try { execSync(cmd, { stdio: 'pipe', timeout: 45000 }); return null; }
    catch (e) { return ((e.stdout || '') + '\n' + (e.stderr || '')).toString().trim(); } };
  if (file.endsWith('.cs')) {
    run(`dotnet format backend/BirraPoint.sln --include "${file}" --no-restore`); // best-effort format
  } else if (/\.(ts|html|scss|css|json)$/.test(file) && file.includes('frontend')) {
    run(`npx prettier --write "${file}"`); // config discovered from frontend/package.json
    if (file.endsWith('.ts') && !file.endsWith('.spec.ts')) {
      const lintErr = run(`npx eslint "${file}" --config frontend/eslint.config.js`);
      if (lintErr) { console.error(`ESLint issues in ${file}:\n${lintErr}\nFix these before continuing.`); process.exit(2); }
    }
  }
  process.exit(0);
});
