import { deserialize, serialize } from 'node:v8';

import { setupZoneTestEnv } from 'jest-preset-angular/setup-env/zone';

setupZoneTestEnv();

// jest-environment-jsdom's sandboxed globalThis doesn't inherit Node's structuredClone (used by
// fake-indexeddb, core/offline/db.spec.ts, T020) — a known jsdom gap. v8 serialize/deserialize is
// Node's own structured-clone algorithm, so this is equivalent, not an approximation.
if (typeof globalThis.structuredClone === 'undefined') {
  globalThis.structuredClone = ((value: unknown) =>
    deserialize(serialize(value))) as typeof structuredClone;
}
