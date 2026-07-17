#!/usr/bin/env node
// PostToolUse warn: nudges re-verification of BR-01 (blind anonymity) when a judge-facing file
// is edited and looks like it references an entrant-identifying field. Heuristic, non-blocking
// in effect (edit already happened) — surfaces as feedback for Claude to act on, same pattern as
// the ESLint check in post-edit.js.
const fs = require('fs');
let input = '';
process.stdin.on('data', d => (input += d));
process.stdin.on('end', () => {
  let file = '';
  try { file = (JSON.parse(input).tool_input || {}).file_path || ''; } catch { process.exit(0); }
  if (!file || !fs.existsSync(file)) process.exit(0);

  const judgeFacing = /(^|[\\/])JudgeDtos[\\/]|(^|[\\/])Features[\\/](TastingOrder|Evaluations|Monitoring)[\\/]|(^|[\\/])Realtime[\\/]CompetitionHub|(^|[\\/])frontend[\\/]src[\\/]app[\\/]features[\\/](judge-|evaluation-sheet|discrepancy)/i;
  if (!judgeFacing.test(file)) process.exit(0);

  let content = '';
  try { content = fs.readFileSync(file, 'utf8'); } catch { process.exit(0); }

  const forbidden = /\bbeerName\b|\bParticipant\b|\bEntryCollaborator\b|\bbrewery\b|\bentrant\b/i;
  const hit = content.match(forbidden);
  if (hit) {
    console.error(
      `Possible BR-01 anonymity violation in ${file}: found "${hit[0]}". ` +
      `Judge-facing DTOs/payloads must be limited to JudgeSampleDto's shape ` +
      `(BeerEntryId, BlindCode, StyleCode, StyleName, SequenceOrder, EvaluationStatus). ` +
      `Load the blind-tasting-integrity skill and verify this is not a leak before continuing.`
    );
    process.exit(2);
  }
  process.exit(0);
});
