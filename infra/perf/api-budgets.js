import http from 'k6/http';
import { check } from 'k6';

// infra/perf/api-budgets.js — Constitution Principle IX API performance budgets (tasks.md T090):
// reads p95 < 200ms, writes p95 < 500ms, exercised against representative endpoints from
// specs/001-birrapoint-mvp/contracts/rest-api.md.
//
// ## Auth setup (read before running)
//
// Neither of this app's two Keycloak clients can mint an ORGANIZER/JUDGE-scoped access token for
// a headless script: `birrapoint-spa` (the app's own OIDC client) has
// `directAccessGrantsEnabled: false` (Authorization Code + PKCE only, infra/keycloak/
// birrapoint-realm.json), and `birrapoint-api-admin`'s client-credentials token is scoped to the
// Keycloak Admin REST API (`manage-users`), not to this app's own API — it carries no
// ORGANIZER/JUDGE realm role. So this script takes pre-obtained bearer tokens via environment
// variables rather than performing its own login. The simplest way to obtain one locally: log in
// through the running app as the seeded `organizer`/`organizer` user (or an invited judge) in a
// browser, open DevTools > Network, and copy the `Authorization: Bearer …` header from any XHR
// the app makes after login (or Application > Session Storage, the keycloak-js token cache).
//
// ## Required environment variables
//
//   API_BASE_URL     e.g. http://localhost:5121/api/v1 (defaults below)
//   ORGANIZER_TOKEN  bearer token for the seeded `organizer` user — drives the GET /competitions
//                    read check
//   JUDGE_TOKEN      bearer token for a judge assigned to TABLE_ID — drives the judge-workspace
//                    read/write checks
//   COMPETITION_ID   the competition TABLE_ID belongs to (used to build the write check's
//                    idempotency key)
//   TABLE_ID         a tastingTable id JUDGE_TOKEN's judge is actually assigned to, with its
//                    order already fixed
//   JUDGE_ID         JUDGE_TOKEN's own judge id (used to build the idempotency key)
//   BEER_ENTRY_ID    a beerEntryId on TABLE_ID that JUDGE_TOKEN's judge has *already submitted* an
//                    evaluation for (evaluations lock permanently on first submit — invariant
//                    5/FR-035, no undo) — see the writes() and setup() comments below for why this
//                    must be an already-submitted sample, not a fresh one, and how that's enforced
//
// ## What writes() actually measures
//
// Every iteration — including the first, gated by setup() below — replays the *same* deterministic
// idempotency key (FR-029/R-07), so this measures the idempotent-replay path (validation, the
// duplicate-key lookup, response marshaling), not a fresh INSERT. That's a deliberate, safety-first
// trade-off: a true first-time-write benchmark would need a brand-new, never-submitted sample on
// every run, which isn't safely repeatable against a real environment without a teardown/reset step
// this script doesn't have. Treat the write budget below as a proxy for the write path's
// non-persistence overhead, not a literal proof of first-insert latency.
//
// ## Run
//
//   k6 run infra/perf/api-budgets.js
//
// against a running local Aspire topology (`dotnet run --project backend/src/BirraPoint.AppHost`).
// `k6` itself is not installed in every environment this repo is checked out in — install it
// separately (https://k6.io/docs/get-started/installation/) before running.

const API_BASE_URL = __ENV.API_BASE_URL || 'http://localhost:5121/api/v1';
const ORGANIZER_TOKEN = __ENV.ORGANIZER_TOKEN;
const JUDGE_TOKEN = __ENV.JUDGE_TOKEN;
const COMPETITION_ID = __ENV.COMPETITION_ID;
const TABLE_ID = __ENV.TABLE_ID;
const JUDGE_ID = __ENV.JUDGE_ID;
const BEER_ENTRY_ID = __ENV.BEER_ENTRY_ID;

const REQUIRED_ENV_VARS = {
  ORGANIZER_TOKEN,
  JUDGE_TOKEN,
  COMPETITION_ID,
  TABLE_ID,
  JUDGE_ID,
  BEER_ENTRY_ID,
};

export const options = {
  scenarios: {
    reads: {
      executor: 'constant-vus',
      exec: 'reads',
      vus: 10,
      duration: '30s',
    },
    writes: {
      executor: 'constant-vus',
      exec: 'writes',
      vus: 5,
      duration: '30s',
      startTime: '30s',
    },
  },
  thresholds: {
    // Guards the p95 budgets below from a false-green: an expired/invalid bearer token yields fast
    // 401s, which would otherwise satisfy 'p(95)<200'/'p(95)<500' while measuring nothing but
    // auth-middleware rejection latency. responseCallback: http.expectedStatuses(...) on every
    // request below marks anything outside the expected 2xx as a failure for this metric, so a
    // broken token (or any other systemic non-2xx) fails the whole run loudly instead of silently
    // passing.
    http_req_failed: ['rate<0.01'],
    // Principle IX: API p95 reads < 200ms, writes < 500ms — tagged per request below so the two
    // budgets are checked independently rather than pooled into one misleading aggregate.
    'http_req_duration{endpoint:read}': ['p(95)<200'],
    'http_req_duration{endpoint:write}': ['p(95)<500'],
  },
};

function authHeaders(token) {
  return { Authorization: `Bearer ${token}` };
}

export function setup() {
  const missing = Object.entries(REQUIRED_ENV_VARS)
    .filter(([, value]) => !value)
    .map(([name]) => name);
  if (missing.length > 0) {
    throw new Error(
      `Missing required env var(s): ${missing.join(', ')}. See the header comment in this ` +
        'file for how to obtain them.',
    );
  }

  // Safety guard (invariant 5/FR-035 — evaluations lock permanently on first submit, no undo):
  // refuse to run writes() at all against a BEER_ENTRY_ID that hasn't already been submitted for
  // this judge, since that would fire a genuine first-time, permanently-locking write into
  // whatever environment this targets on iteration 1, rather than the safe idempotent replay
  // writes() is designed around. This is a read-only check — it never mutates anything itself.
  const samplesRes = http.get(`${API_BASE_URL}/me/tables/${TABLE_ID}/samples`, {
    headers: authHeaders(JUDGE_TOKEN),
  });
  if (samplesRes.status !== 200) {
    throw new Error(
      `setup() could not verify BEER_ENTRY_ID's evaluation status: GET .../samples returned ` +
        `${samplesRes.status} for TABLE_ID=${TABLE_ID}.`,
    );
  }
  const target = samplesRes.json().find((sample) => sample.beerEntryId === BEER_ENTRY_ID);
  if (!target) {
    throw new Error(`BEER_ENTRY_ID ${BEER_ENTRY_ID} is not one of TABLE_ID ${TABLE_ID}'s samples.`);
  }
  if (target.evaluationStatus !== 'Submitted' && target.evaluationStatus !== 'PendingConsensus') {
    throw new Error(
      `BEER_ENTRY_ID ${BEER_ENTRY_ID} has evaluationStatus "${target.evaluationStatus}" — ` +
        'writes() must only ever replay an *already-submitted* evaluation. Submit it once through ' +
        'the real app first (or point this script at a different, already-submitted sample), then ' +
        're-run.',
    );
  }
}

export function reads() {
  // GET /competitions — ORGANIZER read (contracts/rest-api.md §Competitions).
  const competitionsRes = http.get(`${API_BASE_URL}/competitions`, {
    headers: authHeaders(ORGANIZER_TOKEN),
    tags: { endpoint: 'read', name: 'GET /competitions' },
    responseCallback: http.expectedStatuses(200),
  });
  check(competitionsRes, { 'GET /competitions -> 200': (r) => r.status === 200 });

  // GET /me/tables/{tableId}/samples — JUDGE read (contracts/rest-api.md §Judge workspace).
  const samplesRes = http.get(`${API_BASE_URL}/me/tables/${TABLE_ID}/samples`, {
    headers: authHeaders(JUDGE_TOKEN),
    tags: { endpoint: 'read', name: 'GET /me/tables/{tableId}/samples' },
    responseCallback: http.expectedStatuses(200),
  });
  check(samplesRes, { 'GET /me/tables/{tableId}/samples -> 200': (r) => r.status === 200 });
}

export function writes() {
  // POST /me/tables/{tableId}/evaluations — JUDGE write. The idempotency key is the same
  // deterministic value on every iteration (contracts/rest-api.md's
  // `{competitionId}:{tableId}:{judgeId}:{entryId}`, FR-029/R-07). setup()'s guard above ensures
  // BEER_ENTRY_ID is always already-submitted before this ever runs, so every iteration —
  // including the first — hits the stored-result replay branch (`200`), never a fresh insert
  // (`201`) — see the "What writes() actually measures" header note.
  const idempotencyKey = `${COMPETITION_ID}:${TABLE_ID}:${JUDGE_ID}:${BEER_ENTRY_ID}`;
  const payload = JSON.stringify({
    beerEntryId: BEER_ENTRY_ID,
    scores: { aroma: 10, appearance: 2, flavor: 15, mouthfeel: 4, overall: 8 },
    comments: {
      aroma: 'Perf script aroma note, long enough to satisfy the minimum comment length rule.',
      appearance: 'Perf script appearance note, long enough to satisfy the minimum length rule.',
      flavor: 'Perf script flavor note, long enough to satisfy the minimum comment length rule.',
      mouthfeel: 'Perf script mouthfeel note, long enough to satisfy the minimum length rule.',
      overall: 'Perf script overall impression note, long enough to satisfy the minimum length rule.',
    },
  });

  const res = http.post(`${API_BASE_URL}/me/tables/${TABLE_ID}/evaluations`, payload, {
    headers: {
      ...authHeaders(JUDGE_TOKEN),
      'Content-Type': 'application/json',
      'X-Idempotency-Key': idempotencyKey,
    },
    tags: { endpoint: 'write', name: 'POST /me/tables/{tableId}/evaluations' },
    responseCallback: http.expectedStatuses(200, 201),
  });
  check(res, {
    'POST /me/tables/{tableId}/evaluations -> 200/201': (r) => r.status === 200 || r.status === 201,
  });
}
