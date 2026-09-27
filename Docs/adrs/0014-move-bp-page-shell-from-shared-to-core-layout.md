# 0014 - Relocate BpPageShellComponent from shared/components/ to core/layout/

**Status:** Accepted
**Date:** 2026-09-20

## Context

In Feature-Sliced Design, `shared/` holds primitives with no app-specific dependencies and must
never import `core/`. `BpPageShellComponent` started as a layout primitive in `shared/`, but once
it centralized the "Cambiar rol" / "Ajustes" / "Cerrar sesión" actions it injected `Keycloak`,
`ActiveRoleService` and `Router`, becoming the only `shared/` file depending on `core/` (PR #44
review). Extracting just the identity buttons would only move the inversion to another `shared/`
file.

## Decision

Move the component to `core/layout/bp-page-shell/`, keeping class name, selector and inputs.
`BpTopbarComponent` stays in `shared/` and is imported into `core/`, the allowed direction.

## Consequences

- `shared/` has no dependency on `core/` again; the component lives where its responsibilities are.
- No behavior change; consumers only updated import paths.
- A `shared/` component that starts needing `core/` state has outgrown `shared/` and moves to
  `core/` — no exceptions to the rule.
