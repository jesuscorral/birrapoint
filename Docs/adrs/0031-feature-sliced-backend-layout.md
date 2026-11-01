# 0031 - Feature-sliced backend layout

**Status:** Accepted
**Date:** 2026-11-01

## Context
Under `Features/<Feature>/` the backend kept flat files (handlers, validators, endpoints and DTOs side by side, e.g. `JudgeDtos.cs`, `RemoveJudge.cs`). Finding one use case meant scanning a growing folder, DTOs shared by unrelated endpoints blurred slice boundaries, and tests had no predictable mirror of the source.

## Decision
Each feature folder follows one structure:

| Path | Content |
|---|---|
| `Features/<Feature>/Base<Feature>Endpoints.cs` | route group, authorization policy, chains every per-slice `Map...Endpoint()` |
| `Features/<Feature>/Features/<Slice>/` | one use case: command or query + handler, FluentValidation validator, endpoint, slice-only DTOs |
| `Features/<Feature>/CommonComponents/` (+ `Dtos/`) | types shared across slices or features (appliers, rules, shared DTOs) |

- Tests mirror it: `<Feature>ApiTestBase` plus one `*ApiTests` class per slice under `IntegrationTests/<Feature>/Features/<Slice>/`.
- Namespaces equal folders. IDE0130 is enforced; `.claude/scripts/move-namespace.mjs` syncs a move.
- Cross-slice access stays via MediatR messages or `CommonComponents`.
- Exceptions: EF staging entities stay at the `Import` root. The judge-import slice was left flat in `Features/Judges/` at first; ADR-0032 moved it to `Features/Import/`.

## Consequences
- A use case is one folder; endpoint, handler and validator change together.
- Shared code is explicit, so accidental cross-slice coupling shows in review.
- More folders and files; moves need namespace syncs (scripted) and leave stale paths in completed `tasks.md` entries (see the note there).
- Alternative rejected: flat per-feature files (previous layout), which did not scale and gave no test mirror.
