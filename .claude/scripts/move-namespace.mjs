#!/usr/bin/env node
// Re-syncs C# namespaces and `using` directives after files/folders were moved (git mv).
// Pure text pass, no MSBuild/Roslyn load: ~1 s for the whole backend instead of a build loop.
//
//   node .claude/scripts/move-namespace.mjs [--root backend] [--dry-run]
//       Sets every file's namespace to <project name>.<folder path> (the rule IDE0130 enforces) and
//       fixes consumers: adds `using` for types whose namespace changed, drops usings that no
//       longer supply anything, and rewrites fully-qualified references.
//
//   dotnet build backend/BirraPoint.sln 2>&1 | node .claude/scripts/move-namespace.mjs --fix-build
//       Namespaces were already edited by hand: reads CS0246/CS0103 errors from the build output
//       and adds the missing `using` for the (unique) top-level type each error names.
//
// Limits: names declared in more than one namespace are skipped (reported), and a using that only
// supplies extension methods is kept when its namespace holds a static class. `dotnet build` is
// still the final check.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const args = process.argv.slice(2);
const flag = n => args.includes(n);
const opt = (n, d) => (args.includes(n) ? args[args.indexOf(n) + 1] : d);
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const root = path.resolve(repo, opt('--root', 'backend'));
const dryRun = flag('--dry-run');

const SKIP_DIRS = new Set(['bin', 'obj', 'node_modules', '.git', 'TestResults']);
const NS_RE = /^namespace\s+([\w.]+)\s*[;{]/m;
const USING_RE = /^using\s+([\w.]+)\s*;\s*$/;
const GLOBAL_USING_RE = /^global\s+using\s+([\w.]+)\s*;\s*$/;
const TYPE_RE = /^((?:(?:public|internal|sealed|static|abstract|partial|readonly|file|ref|unsafe)\s+)*)(?:class|record(?:\s+struct)?|struct|interface|enum)\s+(\w+)/gm;
const esc = s => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

function walk(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.isDirectory()) { if (!SKIP_DIRS.has(e.name)) walk(path.join(dir, e.name), out); }
    else if (e.name.endsWith('.cs')) out.push(path.join(dir, e.name));
  }
  return out;
}

const projectDirCache = new Map();
function projectOf(file) {
  let dir = path.dirname(file);
  const visited = [];
  while (dir.startsWith(root)) {
    if (projectDirCache.has(dir)) { const hit = projectDirCache.get(dir); visited.forEach(d => projectDirCache.set(d, hit)); return hit; }
    visited.push(dir);
    const csproj = fs.readdirSync(dir).find(f => f.endsWith('.csproj'));
    if (csproj) { const hit = { dir, name: csproj.slice(0, -'.csproj'.length) }; visited.forEach(d => projectDirCache.set(d, hit)); return hit; }
    dir = path.dirname(dir);
  }
  return null;
}

function expectedNamespace(file) {
  const p = projectOf(file);
  if (!p) return null;
  const segs = path.relative(p.dir, path.dirname(file)).split(path.sep).filter(Boolean)
    .map(s => s.replace(/\W/g, '_').replace(/^(\d)/, '_$1'));
  return [p.name, ...segs].join('.');
}

const eolOf = text => (text.includes('\r\n') ? '\r\n' : '\n');
const chainSees = (fileNs, ns) => ns === '' || fileNs === ns || fileNs.startsWith(ns + '.');
const usingKey = ns => (ns === 'System' || ns.startsWith('System.') ? '0' : '1') + ns;

function readFiles() {
  const files = new Map();
  for (const file of walk(root)) {
    let text = fs.readFileSync(file, 'utf8');
    const bom = text.startsWith('﻿');
    if (bom) text = text.slice(1);
    files.set(file, { text, bom, eol: eolOf(text) });
  }
  return files;
}

function indexTypes(files, nsOf) {
  const types = new Map(); // name -> [{ file, ns, isStatic }]
  for (const [file, f] of files) {
    const ns = nsOf(file, f);
    if (ns === null) continue;
    for (const m of f.text.matchAll(TYPE_RE)) {
      const list = types.get(m[2]) ?? [];
      list.push({ file, ns, isStatic: /\bstatic\b/.test(m[1]) });
      types.set(m[2], list);
    }
  }
  return types;
}

function plainUsings(lines) {
  const out = [];
  lines.forEach((l, i) => { const m = USING_RE.exec(l); if (m) out.push({ i, ns: m[1] }); });
  return out;
}

function addUsings(lines, namespaces) {
  for (const ns of namespaces) {
    const existing = plainUsings(lines);
    if (existing.some(u => u.ns === ns)) continue;
    const key = usingKey(ns);
    const before = existing.find(u => usingKey(u.ns) > key);
    if (before) lines.splice(before.i, 0, `using ${ns};`);
    else if (existing.length) lines.splice(existing.at(-1).i + 1, 0, `using ${ns};`);
    else {
      const firstCode = lines.findIndex(l => l.trim() && !l.startsWith('//'));
      lines.splice(Math.max(firstCode, 0), 0, `using ${ns};`, '');
    }
  }
}

function removeUsings(lines, namespaces) {
  for (const u of plainUsings(lines).reverse()) if (namespaces.has(u.ns)) lines.splice(u.i, 1);
  while (lines.length && lines[0].trim() === '') lines.shift();
}

function report(changed, notes) {
  for (const c of changed) console.log(`${dryRun ? 'would change' : 'changed'}: ${path.relative(repo, c.file)}  (${c.what.join(', ')})`);
  for (const n of notes) console.log(`note: ${n}`);
  console.log(`${changed.length} file(s) ${dryRun ? 'would be ' : ''}updated.`);
}

function write(file, f, lines) {
  const out = (f.bom ? '﻿' : '') + lines.join(f.eol);
  if (!dryRun) fs.writeFileSync(file, out, 'utf8');
}

// ---------------------------------------------------------------- --fix-build
if (flag('--fix-build')) {
  const log = fs.readFileSync(0, 'utf8');
  const files = readFiles();
  const types = indexTypes(files, (_, f) => NS_RE.exec(f.text)?.[1] ?? null);
  const wanted = new Map(); // file -> Set<ns>
  const notes = [];
  const errRe = /^(.+?\.cs)\(\d+,\d+\): error CS(?:0246|0103): (?:The type or namespace name|The name) '(\w+)'/gm;
  for (const m of log.matchAll(errRe)) {
    const file = path.resolve(m[1]);
    if (!files.has(file)) continue;
    const found = types.get(m[2]);
    if (!found) { notes.push(`'${m[2]}' not declared as a top-level type in ${path.relative(repo, root)}`); continue; }
    if (found.length > 1) { notes.push(`'${m[2]}' is declared in ${found.length} namespaces; add the using by hand`); continue; }
    if (!wanted.has(file)) wanted.set(file, new Set());
    wanted.get(file).add(found[0].ns);
  }
  const changed = [];
  for (const [file, nss] of wanted) {
    const f = files.get(file);
    const lines = f.text.split(f.eol);
    const before = lines.join('\n');
    addUsings(lines, [...nss].sort((a, b) => usingKey(a).localeCompare(usingKey(b), 'en')));
    if (lines.join('\n') !== before) { write(file, f, lines); changed.push({ file, what: [...nss].map(n => `+using ${n}`) }); }
  }
  report(changed, [...new Set(notes)]);
  process.exit(0);
}

// ---------------------------------------------------------------- sync mode
const files = readFiles();
const pre = new Map();  // file -> declared namespace now
const post = new Map(); // file -> namespace the folder dictates
for (const [file, f] of files) {
  const declared = NS_RE.exec(f.text)?.[1];
  if (!declared) continue;
  pre.set(file, declared);
  post.set(file, expectedNamespace(file) ?? declared);
}

const typesPre = indexTypes(files, file => pre.get(file) ?? null);
const typesPost = indexTypes(files, file => post.get(file) ?? null);

const globals = new Set();
for (const f of files.values()) for (const l of f.text.split(f.eol)) { const m = GLOBAL_USING_RE.exec(l); if (m) globals.add(m[1]); }

const moved = []; // { name, oldNs, newNs }
const affected = new Set(); // namespaces that lost a type
const ambiguous = new Set();
for (const [name, list] of typesPost) {
  if (list.length > 1) { ambiguous.add(name); continue; }
  const oldNs = typesPre.get(name)?.[0].ns;
  if (oldNs !== undefined && oldNs !== list[0].ns) { moved.push({ name, oldNs, newNs: list[0].ns }); affected.add(oldNs); }
}
const nsHasStatic = new Set();
for (const list of typesPost.values()) for (const t of list) if (t.isStatic) nsHasStatic.add(t.ns);

const changed = [];
for (const [file, f] of files) {
  if (!pre.has(file)) continue;
  const preNs = pre.get(file), postNs = post.get(file);
  const lines = f.text.split(f.eol);
  const usings = new Set(plainUsings(lines).map(u => u.ns));
  const ids = new Set(f.text.match(/\b[A-Za-z_]\w*\b/g));
  const what = [];

  const toAdd = new Set();
  const sees = (ns, fileNs) => chainSees(fileNs, ns) || usings.has(ns) || globals.has(ns);
  for (const id of ids) {
    const t = typesPost.get(id);
    if (!t || t.length !== 1 || t[0].file === file) continue;
    const oldNs = typesPre.get(id)?.[0].ns ?? t[0].ns, newNs = t[0].ns;
    if (oldNs === newNs && preNs === postNs) continue;
    if (sees(oldNs, preNs) && !sees(newNs, postNs)) toAdd.add(newNs);
  }

  const toRemove = new Set();
  for (const u of usings) {
    if (!affected.has(u) || nsHasStatic.has(u)) continue;
    const needed = [...ids].some(id => typesPost.get(id)?.some(t => t.ns === u && t.file !== file));
    if (!needed) toRemove.add(u);
  }
  for (const ns of toAdd) toRemove.delete(ns);

  let body = lines.join(f.eol);
  if (preNs !== postNs) { body = body.replace(NS_RE, m => m.replace(preNs, postNs)); what.push(`namespace ${preNs} -> ${postNs}`); }
  for (const m of moved) {
    const fq = `${m.oldNs}.${m.name}`;
    if (m.oldNs && body.includes(fq)) { body = body.replace(new RegExp(`\\b${esc(fq)}\\b`, 'g'), `${m.newNs}.${m.name}`); what.push(`qualified ${m.name}`); }
  }
  const edited = body.split(f.eol);
  if (toRemove.size) { removeUsings(edited, toRemove); toRemove.forEach(n => what.push(`-using ${n}`)); }
  if (toAdd.size) { addUsings(edited, [...toAdd].sort((a, b) => usingKey(a).localeCompare(usingKey(b), 'en'))); toAdd.forEach(n => what.push(`+using ${n}`)); }

  if (what.length) { write(file, f, edited); changed.push({ file, what }); }
}

const notes = [];
if (ambiguous.size) notes.push(`skipped ${ambiguous.size} type name(s) declared in several namespaces: ${[...ambiguous].slice(0, 8).join(', ')}${ambiguous.size > 8 ? ', ...' : ''}`);
report(changed, notes);
