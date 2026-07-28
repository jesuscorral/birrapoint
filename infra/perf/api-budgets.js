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
//   BEER_ENTRY_ID    a beerEntryId on TABLE_ID that is (or was) JUDGE_TOKEN's *first* reachable
//                    sample in the fixed order — see the writes() comment below for why reusing
//                    an already-submitted sample, not a fresh one, is what makes this safely
//                    repeatable under load
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
}

export function reads() {
  // GET /competitions — ORGANIZER read (contracts/rest-api.md §Competitions).
  const competitionsRes = http.get(`${API_BASE_URL}/competitions`, {
    headers: authHeaders(ORGANIZER_TOKEN),
    tags: { endpoint: 'read', name: 'GET /competitions' },
  });
  check(competitionsRes, { 'GET /competitions -> 200': (r) => r.status === 200 });

  // GET /me/tables/{tableId}/samples — JUDGE read (contracts/rest-api.md §Judge workspace).
  const samplesRes = http.get(`${API_BASE_URL}/me/tables/${TABLE_ID}/samples`, {
    headers: authHeaders(JUDGE_TOKEN),
    tags: { endpoint: 'read', name: 'GET /me/tables/{tableId}/samples' },
  });
  check(samplesRes, { 'GET /me/tables/{tableId}/samples -> 200': (r) => r.status === 200 });
}

export function writes() {
  // POST /me/tables/{tableId}/evaluations — JUDGE write. The idempotency key is the same
  // deterministic value on every iteration (contracts/rest-api.md's
  // `{competitionId}:{tableId}:{judgeId}:{entryId}`, FR-029/R-07): the first call is a fresh
  // insert, every call after that is a stored-result replay (`200`, not `201`) rather than a
  // fresh write attempt against FR-022's strict-sequencing/order-fixed preconditions — which is
  // exactly what makes this endpoint safely repeatable at load without corrupting the fixture or
  // needing a fresh never-evaluated sample per iteration.
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
  });
  check(res, {
    'POST /me/tables/{tableId}/evaluations -> 200/201': (r) => r.status === 200 || r.status === 201,
  });
}
