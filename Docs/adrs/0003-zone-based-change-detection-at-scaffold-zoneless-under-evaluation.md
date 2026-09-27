# 0003 - Zone-based change detection at scaffold time; zoneless adoption under evaluation

**Status:** Proposed
**Date:** 2026-07-07

## Context

The workspace was scaffolded with the CLI default: zone-based change detection
(`provideZoneChangeDetection({ eventCoalescing: true })`, `zone.js` in `polyfills`, zone Jest
environment). Angular 20 also offers stable `provideZonelessChangeDetection()`, which fits the
Signals-first mandate and removes `zone.js` (~11 kB gzip) from the initial bundle.

## Decision

Keep zone-based change detection (current state). Switching to zoneless means
`provideZonelessChangeDetection()`, removing `zone.js` from polyfills and moving Jest to
`setupZonelessTestEnv`, after verifying `keycloak-angular`, CDK drag-drop and SignalR callbacks
work without zone patching.

## Consequences

- Components must stay signal-first and not rely on zone-triggered change detection, so the switch
  stays cheap.
- If adopted: third-party callbacks (SignalR, Keycloak, Dexie) must update signals, not mutate
  state silently.
- If rejected: record the blocking incompatibility here and make `OnPush` mandatory.
