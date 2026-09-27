# 0002 - Pin Angular-ecosystem packages to the workspace's Angular major line

**Status:** Accepted
**Date:** 2026-07-07

## Context

The workspace is pinned to Angular 20 (R-02). Installing companion packages at `latest` failed
with `ERESOLVE`: `@angular/cdk@latest` already required Angular 22+. Packages that version in
lockstep with Angular silently track a newer framework, and `--force`/`--legacy-peer-deps` would
hide real incompatibilities.

## Decision

Packages whose major tracks Angular's (`@angular/cdk@^20`, `keycloak-angular@^20`, and any future
one such as `@angular/material`) stay on the workspace's Angular major and are upgraded only
together with Angular (`ng update`). Independently versioned packages (`keycloak-js`, `dexie`,
`@microsoft/signalr`, `tailwindcss`) follow their own latest stable.

## Consequences

- Reproducible installs with no peer-dependency overrides.
- Security fixes shipped only on newer majors require an Angular upgrade to consume.
- Check the Angular compatibility range of any new Angular-adjacent tool (angular-eslint,
  jest-preset-angular) before adopting it; grouping these packages in Dependabot config keeps the
  pin automatic.
