#!/usr/bin/env node
// PreToolUse guard: blocks destructive or workflow-breaking shell commands before they run.
// The command is split into segments (newlines, ;, &, &&, ||, |) and rules are anchored at each
// segment start, so text that merely mentions a command (commit messages, PR bodies, echo) is not
// blocked. Heredoc bodies are data unless fed to a shell. Strings run by `bash -c`, `pwsh -Command`,
// `cmd /c`, `$(...)` and backticks are checked as commands too.
// Exit 2 = blocked, the message is shown to Claude. Tests: node .claude/hooks/guard.test.js
const { execSync } = require('child_process');

let input = '';
process.stdin.on('data', d => (input += d));
process.stdin.on('end', () => {
  let data = {};
  try { data = JSON.parse(input); } catch { process.exit(0); }
  const cmd = (data.tool_input || {}).command || '';
  const cwd = data.cwd || process.env.CLAUDE_PROJECT_DIR || process.cwd();

  // Current branch, looked up only when a branch rule needs it. GUARD_CURRENT_BRANCH is for tests.
  let branch;
  const currentBranch = () => {
    if (branch !== undefined) return branch;
    if (process.env.GUARD_CURRENT_BRANCH !== undefined) return (branch = process.env.GUARD_CURRENT_BRANCH);
    try {
      branch = execSync('git rev-parse --abbrev-ref HEAD', { cwd, stdio: ['ignore', 'pipe', 'ignore'], timeout: 5000 })
        .toString().trim();
    } catch { branch = ''; }
    return branch;
  };
  const onMain = () => /^(main|master)$/.test(currentBranch());

  const SHELL = /^(bash|sh|zsh|pwsh|powershell|cmd)(\.exe)?\b/i;

  // --- Secret reads: token based, on the original (unblanked) text so quoted paths count.
  const READERS = /^(cat|type|less|more|head|tail|bat|nl|gc|get-content|select-string|grep|egrep|fgrep|rg|sed|awk|base64|xxd|od|strings|cp|copy|copy-item|scp|tar|zip)$/i;
  const SECRET_TOKENS = [
    /(^|[\/\\:=])\.env(\.[\w.-]+)?$/i,
    /\.tfstate(\.backup)?$/i,
    /(^|[\/\\:])terraform\.tfvars$/i,
    /(^|[\/\\:])secrets\.json$/i,
    /appsettings\.Production\.json$/i,
    /\.aws[\/\\]credentials$/i,
    /(^|[\/\\])\.azure([\/\\]|$)/i,
    /\.pfx$/i
  ];
  const TEMPLATE = /\.(example|sample|template|dist)$/i;
  function readsSecret(seg, rawSeg) {
    const words = seg.split(/\s+/);
    const isGitShow = /^git$/i.test(words[0]) && /^(show|cat-file)$/i.test(words[1] || '');
    if (!READERS.test(words[0]) && !isGitShow) return false;
    let tokens = rawSeg.split(/\s+/).slice(isGitShow ? 2 : 1)
      .map(t => t.replace(/["']/g, '')).filter(t => t && !t.startsWith('-'));
    if (/^(cp|copy|copy-item|scp)$/i.test(words[0])) tokens = tokens.slice(0, -1); // destination is a write
    return tokens.some(t => !TEMPLATE.test(t) && SECRET_TOKENS.some(re => re.test(t)));
  }

  // --- git push: refspecs (after the remote) whose destination is main, or the current branch on main.
  function pushPositional(seg) { return seg.split(/\s+/).slice(2).filter(t => !t.startsWith('-')); }
  function pushTargetsMain(seg) {
    return pushPositional(seg).slice(1).some(r =>
      /^(main|master)$/.test(r.split(':').pop().replace(/^\+/, '').replace(/^refs\/heads\//, '')));
  }
  function pushesCurrentBranch(seg) {
    const p = pushPositional(seg);
    return p.length <= 1 || p.slice(1).some(r => r.replace(/^\+/, '').split(':')[0] === 'HEAD' && !r.includes(':'));
  }

  // aws, optionally .exe, then any global flags (with an optional value) before the service.
  const aws = String.raw`^aws(\.exe)?(\s+--?[\w-]+(\s+(?!-)\S+)?)*`;
  const ghWrite = /\s(-X|--method)\s*(POST|PUT|PATCH|DELETE)\b|\s(-f|-F|--field|--raw-field|--input)\b/i;

  // [test, message]; test is a regex on the normalized segment or a function (seg, rawSeg) => bool.
  const rules = [
    [/^rm\s+(-[a-z]*r[a-z]*f|-[a-z]*f[a-z]*r)\s+["']?[\/~]/i, 'Blocked: recursive force delete on root/home path.'],
    [/^git\s+push\b.*(--force(?!-with-lease)|\s-f\b)/i, 'Blocked: force push. Use --force-with-lease on a feature branch only if truly needed.'],
    [s => /^git\s+push\b/i.test(s) && pushTargetsMain(s), 'Blocked: direct push to main. Push a feature branch and open a PR.'],
    [/^git\s+.*--no-verify\b/i, 'Blocked: skipping git hooks (--no-verify) is not allowed in this repo.'],
    [/^git\s+commit\b.*\s-[a-zA-Z]*n[a-zA-Z]*(\s|$)/, 'Blocked: `git commit -n` skips git hooks (same as --no-verify).'],
    [/^git\s+reset\s+--hard/i, 'Blocked: hard reset. Use git stash or restore specific files.'],
    [/^git\s+clean\s+-[a-z]*f/i, 'Blocked: git clean -f deletes untracked files. Ask the user first.'],
    [/^gh\s+pr\s+merge\b/i, 'Blocked: PRs are merged by a human (CLAUDE.md workflow step 5).'],
    [/^gh\s+pr\s+review\b.*--(approve|request-changes)\b|^gh\s+pr\s+review\b.*\s-[ar]\b/i, 'Blocked: never approve or request changes; post findings as a plain comment (`gh pr comment`).'],
    [s => /^gh\s+api\b.*\/pulls\/\d+\/(merge|reviews)\b/i.test(s) && ghWrite.test(s), 'Blocked: merging or reviewing PRs through the API is for humans only.'],
    [/^dotnet\s+ef\s+database\s+drop/i, 'Blocked: database drop. Ask the user to run this manually.'],
    [/^(docker\s+exec\s.*)?psql\b.*\bdrop\s+(database|table|schema)\s/i, 'Blocked: destructive SQL. Ask the user to run this manually.'],
    [/^terraform\b(\s+-chdir=\S+)?\s+(apply|destroy)\b/i, 'Blocked: terraform apply/destroy changes real cloud resources. Ask the user to run it (or use terraform plan).'],
    [/^((pwsh|powershell)(\.exe)?\s+(-\S+\s+)*)?\S*teardown\.ps1\b(?!.*-WhatIf(?!:\$?false))/i, 'Blocked: teardown.ps1 deletes the whole cloud environment (Azure or AWS). Run with -WhatIf or ask the user.'],
    [/^((pwsh|powershell)(\.exe)?\s+(-\S+\s+)*)?\S*teardown\.ps1\b.*-WhatIf:\$?false/i, 'Blocked: teardown.ps1 with -WhatIf switched off deletes the whole cloud environment. Ask the user.'],
    [/^az\s+(group|keyvault|containerapp)\s+delete\b/i, 'Blocked: deleting Azure resources. Ask the user to run it manually.'],
    [new RegExp(aws + String.raw`\s+[\w-]+\s+(batch-)?(delete|terminate|remove|deregister|detach|revoke|purge)-`, 'i'), 'Blocked: deleting or detaching AWS resources. Ask the user to run it manually.'],
    [new RegExp(aws + String.raw`\s+s3\s+(rm|rb)\b`, 'i'), 'Blocked: deleting S3 objects or buckets. Ask the user to run it manually.'],
    [new RegExp(aws + String.raw`\s+s3\s+sync\b.*\s--delete\b`, 'i'), 'Blocked: aws s3 sync --delete removes objects. Ask the user to run it manually.'],
    [/^docker\s+(volume\s+(rm|prune)|system\s+prune)/i, 'Blocked: removing Docker volumes wipes local Postgres/Keycloak data. Ask the user first.'],
    [/^(pwsh|powershell)(\.exe)?\b.*\s-(e|ec|enc|encodedcommand)\b/i, 'Blocked: encoded PowerShell commands cannot be checked. Run the command in plain text.'],
    [readsSecret, 'Blocked: reading secrets (.env, tfstate, terraform.tfvars, secrets.json, production settings, cloud credentials). Ask the user if a value is really needed.'],
    [s => /^git\s+commit\b/i.test(s) && onMain(), 'Blocked: committing on main. Create a feature/<task-id> (or chore/<topic>) branch first.'],
    [s => /^git\s+push\b/i.test(s) && onMain() && pushesCurrentBranch(s), 'Blocked: this push would update main. Push a feature branch and open a PR.']
  ];

  // Heredoc bodies are data (commit messages, PR bodies, files) unless the heredoc feeds a shell.
  function dropHeredocBodies(text) {
    const out = [];
    let tag = null;
    for (const line of text.split(/\r?\n/)) {
      if (tag !== null) {
        if (line.trim() === tag.name) tag = null;
        else if (tag.keep) out.push(line);
        continue;
      }
      out.push(line);
      const m = line.match(/<<-?\s*["']?(\w+)["']?/);
      if (m) tag = { name: m[1], keep: SHELL.test(line.trim().replace(/^((sudo|env)\s+)+/, '')) };
    }
    return out.join('\n');
  }

  // Same-length blanking keeps offsets aligned between the blanked and the original text.
  const blank = (text, re) => text.replace(re, q => q[0] + '_'.repeat(q.length - 2) + q[q.length - 1]);
  const QUOTED = /"(?:[^"\\]|\\.)*"|'[^']*'/g;
  const WRAPPERS = /^((sudo|time|env)\s+|[A-Za-z_][A-Za-z0-9_]*=\S*\s+|\.\s+)+/;
  // git global options before the subcommand (`git -C dir push`, `git -c k=v commit`).
  const GIT_GLOBALS = /^git\s+((-C|-c)\s+\S+\s+|--(git-dir|work-tree|namespace)(=\S+|\s+\S+)\s+|--no-pager\s+|--bare\s+)+/i;

  function check(command, depth) {
    if (depth > 4) return null;
    const raw = dropHeredocBodies(command);
    const blanked = blank(raw, QUOTED);

    // $(...) and backticks run even inside double quotes; single-quoted text never runs.
    const noSingle = blank(raw, /'[^']*'/g);
    for (const m of noSingle.matchAll(/\$\(([^()]*)\)|`([^`]*)`/g)) {
      const msg = check(m[1] ?? m[2], depth + 1);
      if (msg) return msg;
    }

    const sep = /\r?\n|&&|\|\||[;&|]/g;
    let start = 0;
    const bounds = [];
    for (const m of blanked.matchAll(sep)) { bounds.push([start, m.index]); start = m.index + m[0].length; }
    bounds.push([start, blanked.length]);

    for (const [a, b] of bounds) {
      const segB = blanked.slice(a, b);
      const lead = segB.length - segB.trimStart().length;
      const wrap = (segB.trimStart().match(WRAPPERS) || [''])[0].length;
      let seg = segB.slice(lead + wrap).trimEnd();
      const rawSeg = raw.slice(a + lead + wrap, b).trimEnd();
      if (!seg) continue;
      if (/^git\s+.*\s-c\s+core\.hookspath\b/i.test(seg)) return 'Blocked: overriding core.hooksPath skips git hooks.';
      seg = seg.replace(GIT_GLOBALS, 'git ');

      // Nested shells: check the string they run.
      if (SHELL.test(seg)) {
        const arg = seg.match(/\s(-c|-command|\/c|\/k)\s+(\S.*)$/i);
        if (arg) {
          let inner = rawSeg.slice(seg.length - arg[2].length + (rawSeg.length - seg.length));
          if (/^["']/.test(inner)) inner = inner.slice(1, inner.lastIndexOf(inner[0]) > 0 ? inner.lastIndexOf(inner[0]) : undefined).replace(/\\(["\\])/g, '$1');
          const msg = check(inner, depth + 1);
          if (msg) return msg;
        }
      }
      for (const [test, msg] of rules) {
        if (typeof test === 'function' ? test(seg, rawSeg) : test.test(seg)) return msg;
      }
    }
    return null;
  }

  const msg = check(cmd, 0);
  if (msg) { console.error(msg); process.exit(2); }
  process.exit(0);
});
