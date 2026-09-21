# 0014 - Relocate BpPageShellComponent from shared/components/ to core/layout/

**Status:** Accepted
**Date:** 2026-09-20

## Context

`plan.md`'s Feature-Sliced Design convention (also stated in this repo's own root `CLAUDE.md`)
draws a one-way dependency: `core/` holds cross-cutting infrastructure (auth, api, realtime,
offline), `features/` holds business screens, and `shared/` holds primitives — presentational
building blocks with no app-specific or cross-cutting dependencies, so they stay reusable and
testable in isolation from the rest of the app.

`BpPageShellComponent` started (T127/FR-061) as exactly that kind of primitive: topbar + `main`
landmark + gutter rules, extracted from the wizard shell. It lived at
`shared/components/bp-page-shell/`, consistent with its neighbors (`bp-button`, `bp-input`,
`bp-topbar`, …).

This session (2026-09-20) centralized the "Cambiar rol" / "Ajustes" / "Cerrar sesión" identity
actions into it, so every screen wrapped in `<bp-page-shell>` gets them for free instead of each
consumer projecting a byte-for-byte duplicate (a real finding from PR #43's review). Doing that
made the component inject `Keycloak`, `ActiveRoleService` (`core/auth/`), and `Router` directly —
it now owns real cross-cutting identity/navigation logic, not just layout. That made it, as of
this change, the only file under `shared/` importing from `core/`: an FSD layering inversion
(senior-code-reviewer finding A1 on PR #44). `shared/` importing `core/` lets a "primitive"
secretly depend on app-specific infrastructure, defeating the point of the layer split — a test
for `shared/bp-button` shouldn't need to know Keycloak exists, and neither should a shared-layer
component in principle.

Two fixes were on the table: (a) extract just the identity-action buttons into their own small
component and keep `BpPageShellComponent` itself layout-only in `shared/`, or (b) accept that the
component's job description changed and relocate/rename it into `core/`. Option (a) doesn't
actually resolve the inversion — the extracted identity-actions component would still need
`Keycloak`/`ActiveRoleService`, so it would still need to live somewhere with access to them; if
that somewhere is `shared/`, the same violation just moved down one file.

## Decision

Move the component from `shared/components/bp-page-shell/` to `core/layout/bp-page-shell/`,
keeping its class name (`BpPageShellComponent`), selector (`bp-page-shell`), and template
unchanged — only its file location and its own imports' relative paths change. `core/` already
free to depend on other `core/` modules, so `ActiveRoleService` and `Router` stop being an
inversion; `BpTopbarComponent` (still a genuine `shared/` primitive — no cross-cutting
dependencies of its own) is imported down into `core/layout/`, which is the FSD-sanctioned
direction. All 12 consumers (`features/**`) update their import path from
`../../shared/components/bp-page-shell/bp-page-shell.component` to
`../../core/layout/bp-page-shell/bp-page-shell.component`; the `shared/components/index.ts` barrel
drops its `BpPageShellComponent` re-export.

Renaming the selector/class (e.g. to `app-shell`) was considered and deliberately skipped: it
would touch every `<bp-page-shell>` usage across those same 12 templates for no functional gain,
widening the diff's blast radius without fixing anything the layering move doesn't already fix.

## Consequences

- **Positive**: `shared/` no longer has any file depending on `core/` — the FSD dependency rule
  (`core`/`shared` → depended on by `features`; `shared` never depends on `core`) holds again
  without exception.
- **Positive**: the component's new location matches what it actually is now — app shell chrome
  wired to identity and navigation state, not a decontextualized visual primitive like `bp-button`
  or `bp-input`.
- **Neutral**: no runtime/behavioral change — verified via the full Jest suite (787/787 passing
  post-move) and `ng lint`/`format:check`/`build:budget` all green. Selector and public API
  (`homeLink`, `title` inputs) are untouched, so every consumer's template needed zero changes.
- **Negative**: a 12-file mechanical import-path diff, and any future direct link to this file's
  old path (docs, IDE bookmarks) is now stale — `Docs/arquitectura_viva.md` was updated in the
  same change.
- **Review trigger**: if a future `shared/` component starts needing `core/`-level state again,
  treat that as the same signal — it has outgrown `shared/` and should move to `core/` (or a new
  `core/` subdirectory), not gain an exception to this rule.
