# 0003 - Zone-based change detection at scaffold time; zoneless adoption under evaluation

**Status:** Proposed
**Date:** 2026-07-07

## Context

The Angular 20 workspace was scaffolded (T003) with the CLI default: zone-based change
detection (`provideZoneChangeDetection({ eventCoalescing: true })` in `app.config.ts`, `zone.js`
in the `polyfills` array, and the zone test environment in `setup-jest.ts`). Angular 20 also
ships stable `provideZonelessChangeDetection()`. The constitution mandates Signals-first
reactivity (Principle II/frontend conventions) and hard performance budgets (Principle IX:
initial JS ≤ 500 KB gzipped, interactive < 3 s on 4G); `zone.js` accounts for the entire
polyfills chunk (~34.6 kB raw / ~11.3 kB transfer in the first build). The automated review of
PR #2 recommended deciding this before Phase 2/3 feature work, because retrofitting zoneless
across implemented feature slices is a migration, while adopting it on a greenfield shell is
nearly free.

## Decision

Keep the zone-based default for the Phase 1 scaffold (this is what is currently implemented).
**Before the first frontend feature story begins (Phase 3 / US1), evaluate and decide** whether
to switch to `provideZonelessChangeDetection()`, remove `zone.js` from the polyfills, and move
Jest to `setupZonelessTestEnv`. The working recommendation is to adopt zoneless, pending
verification that the pinned ecosystem packages (`keycloak-angular@20`, CDK drag-drop,
`@microsoft/signalr` callbacks) behave correctly without zone patching.

## Consequences

- **While undecided**: all new components must be written signal-first and compatible with
  either mode (no reliance on zone-driven implicit change detection), so the eventual switch
  stays cheap.
- **If adopted**: polyfills chunk disappears from the initial bundle; change detection runs
  only on signal/notification boundaries — third-party callbacks (SignalR, Keycloak adapter,
  Dexie events) must set signals rather than mutate state silently.
- **If rejected**: document the blocking incompatibility here and mark this ADR Rejected;
  `OnPush` strategy then becomes mandatory on every component to protect the performance
  budgets.
