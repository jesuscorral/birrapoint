#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { gzipSync } from 'node:zlib';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Enforces Constitution Principle IX's literal "initial JS bundle ≤ 500 KB gzipped" budget.
//
// angular.json's own `budgets` config (production configuration) checks *raw* build output
// bytes, not gzip — a naive `maximumError: "500kB"` there would enforce something stricter than
// the actual invariant (raw JS/CSS is typically 3-4x its gzipped size). This script is the real
// enforcement point: it reads the built app's own index.html to find exactly the files the
// browser loads on first paint (the same "Initial chunk files" set `ng build` itself reports —
// main/polyfills/global-styles chunks; anything not referenced directly in index.html is a
// lazily-loaded route chunk, out of scope for this budget), gzips each one the way a real HTTP
// server with compression enabled would serve it, and fails if the total exceeds 500 KB.
// angular.json's raw-byte budget stays in place as an early, cheaper warning signal — this
// script is what actually gates the constitution's number.
//
// Usage: node scripts/check-bundle-budget.mjs [pathToIndexHtml]
//   Run after `ng build` (production). Defaults to dist/birrapoint/browser/index.html.

const GZIP_BUDGET_BYTES = 500 * 1024;

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = path.resolve(scriptDir, '..');
const indexHtmlPath = process.argv[2]
  ? path.resolve(process.cwd(), process.argv[2])
  : path.join(frontendRoot, 'dist', 'birrapoint', 'browser', 'index.html');
const browserDir = path.dirname(indexHtmlPath);

let indexHtml;
try {
  indexHtml = readFileSync(indexHtmlPath, 'utf-8');
} catch (error) {
  console.error(`Could not read ${indexHtmlPath} — run "ng build" first.\n${error.message}`);
  process.exit(1);
}

// Reads a single named attribute out of one already-isolated tag string, independent of where it
// falls among the tag's other attributes — a fixed-order regex across the whole tag (e.g.
// requiring `rel` before `href`) silently misses a tag the moment attribute order differs, which
// is exactly the kind of failure that should be loud, not a quietly-under-counted budget.
function readAttr(tag, attrName) {
  const match = new RegExp(`\\s${attrName}="([^"]*)"`, 'i').exec(tag);
  return match ? match[1] : null;
}

// Every file the browser actually fetches on first paint: <script src="…"> and
// <link rel="stylesheet"|"modulepreload" href="…"> in the built index.html (a Set dedupes the
// <noscript> fallback's duplicate stylesheet link). `modulepreload` is included alongside
// `stylesheet` since Angular can emit it for eagerly-needed chunks — today's build has none (this
// app has zero lazy routes), but the parsing has to hold up the day that changes, not just today.
const initialFiles = new Set();
for (const tagMatch of indexHtml.matchAll(/<script\b[^>]*>/gi)) {
  const src = readAttr(tagMatch[0], 'src');
  if (src) {
    initialFiles.add(src);
  }
}
for (const tagMatch of indexHtml.matchAll(/<link\b[^>]*>/gi)) {
  const tag = tagMatch[0];
  const rel = (readAttr(tag, 'rel') ?? '').toLowerCase();
  if (rel !== 'stylesheet' && rel !== 'modulepreload') {
    continue;
  }
  const href = readAttr(tag, 'href');
  if (href) {
    initialFiles.add(href);
  }
}

if (initialFiles.size === 0) {
  console.error(
    `No <script src> / <link rel="stylesheet"|"modulepreload"> references found in ${indexHtmlPath}.`,
  );
  process.exit(1);
}

function formatKb(bytes) {
  return `${(bytes / 1024).toFixed(2)} kB`;
}

let totalRaw = 0;
let totalGzip = 0;
const rows = [];

for (const file of initialFiles) {
  // Initial-chunk references are always same-origin relative paths (no external CDN scripts in
  // this app) — an absolute/external URL here would mean this parsing assumption no longer holds.
  if (/^[a-z]+:\/\//i.test(file)) {
    console.error(`Unexpected external URL in initial chunk references: ${file}`);
    process.exit(1);
  }

  const raw = readFileSync(path.join(browserDir, file));
  const gzip = gzipSync(raw, { level: 9 });
  totalRaw += raw.length;
  totalGzip += gzip.length;
  rows.push({ file, raw: raw.length, gzip: gzip.length });
}

console.log('Initial bundle budget check (gzip, Constitution Principle IX)');
console.log('');
for (const row of rows.sort((a, b) => b.gzip - a.gzip)) {
  console.log(
    `  ${row.file.padEnd(28)} raw ${formatKb(row.raw).padStart(12)}  gzip ${formatKb(row.gzip).padStart(12)}`,
  );
}
console.log('');
console.log(
  `  ${'Total'.padEnd(28)} raw ${formatKb(totalRaw).padStart(12)}  gzip ${formatKb(totalGzip).padStart(12)}`,
);
console.log('');

if (totalGzip > GZIP_BUDGET_BYTES) {
  console.error(
    `FAIL: initial bundle is ${formatKb(totalGzip)} gzipped, exceeding the ${formatKb(GZIP_BUDGET_BYTES)} budget by ${formatKb(totalGzip - GZIP_BUDGET_BYTES)}.`,
  );
  process.exit(1);
}

console.log(
  `PASS: initial bundle is ${formatKb(totalGzip)} gzipped, within the ${formatKb(GZIP_BUDGET_BYTES)} budget.`,
);
