---
name: "scoring-and-discrepancy"
description: "BJCP score caps, comment-length rule, and the >7-point discrepancy/consensus math (FR-031/FR-032/FR-042). Load before touching the evaluation sheet, SubmitEvaluation, CloseTable, or discrepancy slices."
user-invocable: true
disable-model-invocation: false
---

# Scoring rules

Section caps, fixed, never editable by any actor: **Aroma 12 / Appearance 3 / Flavor 20 /
Mouthfeel 5 / Overall 10**. Total ≤ 50 is always computed server-side (a DB computed column per
T009), never accepted as client input.

Comment requirement: every section comment ≥ 20 characters. Enforce identically client-side (live
remaining-length UI, T059) and server-side (validator — never trust the client length check
alone).

Sequential gating: evaluation requires competition state `InEvaluation`, the table's order fixed,
and samples submitted strictly in fixed sequence — submitting entry N+1 before N is rejected, not
reordered.

# Discrepancy consensus (FR-031/FR-032)

- On submit, pairwise-compare this judge's total against every other submitted total for the same
  sample on the same table. Any pair >7 points apart flags the sample `PendingConsensus` and
  blocks table close — this holds for both the 2-judge and ≥3-judge case (pairwise, not
  average-based).
- Resolution: an alert resolves only when all totals for that sample are within 7 points of each
  other pairwise. Only judges involved in the open alert may `PUT` (adjust) their evaluation while
  it's open — everyone else stays locked-on-submit.
- Single-judge tables never raise a discrepancy — there's no second total to compare against.

# Consolidated mean (FR-042)

Computed once at `CloseTable` (T064): the mean of all valid totals for a sample — not live-updated
per submission.

# Immutability boundary

Once a table is closed, only an organizer `CorrectEvaluation` (T065) can change a total — always
re-validates caps/lengths, recomputes total and mean, and writes a before/after `AuditLog` row.
Judges never regain write access after close.
