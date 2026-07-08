# 0002 - Pin Angular-ecosystem packages to the workspace's Angular major line

**Status:** Accepted
**Date:** 2026-07-07

## Context

The workspace is pinned to Angular 20 (R-02: plans must pin versions for reproducibility).
During task T003, installing companion packages at their npm `latest` tag failed with an npm
`ERESOLVE` peer-dependency conflict: `@angular/cdk@latest` is already 22.x and requires
`@angular/common@^22 || ^23`. Angular-ecosystem packages version in lockstep with Angular
majors, so "latest" silently tracks a newer framework than the one this project uses.
Overriding with `--force`/`--legacy-peer-deps` would accept broken resolutions and hide real
incompatibilities.

## Decision

Every package whose major version tracks the Angular major MUST be installed and kept on the
line matching the workspace's Angular major — currently `@angular/cdk@^20` and
`keycloak-angular@^20`. These packages are only upgraded together with an Angular major upgrade
(`ng update`), never independently. Packages with independent versioning (`keycloak-js`,
`dexie`, `@microsoft/signalr`, `tailwindcss`) follow their own latest stable.

## Consequences

- **Positive**: reproducible installs with no peer-dependency overrides; framework and
  companion libraries stay API-compatible by construction.
- **Negative**: security patches published only on newer majors of these packages require an
  Angular major upgrade to consume; the pin must be remembered when Angular is upgraded
  (grouping them in future Renovate/Dependabot config would automate this).
- Applies to any future Angular-lockstep dependency (e.g. `@angular/material`,
  `angular-eslint`, `jest-preset-angular` compatibility ranges must be checked against the
  Angular major before adoption).
