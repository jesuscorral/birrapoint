Closes #<!-- issue number -->

## What and why

<!-- 2-3 sentences: what changes and why. Cite the requirement (FR-/SC-/US-/ADR-).
     Required: the PR check fails while this section is empty. -->

## How to review

<!-- Suggested reading order. Separate the key files from mechanical changes (renames,
     formatting, generated code), e.g. "724 pure renames; review only
     `git diff main...HEAD -M --name-status | grep -v '^R100'`". -->

## Changes

<!-- One line per area; delete the areas this PR doesn't touch. -->
- **Backend:**
- **Frontend:**
- **Infra / CI:**
- **Docs:** <!-- contracts, quickstart.md, ADR, Docs/arquitectura_viva.md -->

## Invariants touched

<!-- Tick what this PR affects and say how it is covered; tick "None" otherwise. -->
- [ ] Blind anonymity (judge DTOs / SignalR payloads carry no entrant fields)
- [ ] Idempotent sync, immutability or the competition state machine
- [ ] Scoring rules (caps, totals, discrepancies)
- [ ] Security (authorization policies, secrets, input validation)
- [ ] EF Core migration (reviewed with `migration-safety`)
- [ ] Accessibility (axe) or performance budgets
- [ ] None

## Evidence

<!-- Results of the gates you ran; say why a gate could not run and what you verified instead. -->
| Gate | Result |
|---|---|
| `dotnet build` / `dotnet format` | |
| Unit / integration tests | |
| Jest / lint / bundle budget | |
| E2E (`quickstart.md` scenario) | |

## Risks and rollback

<!-- Deployment order, migrations, breaking contract changes, how to roll back. "None" is fine. -->

<details>
<summary>Screenshots / logs</summary>

<!-- UI changes: before/after screenshots. Long logs go here. -->

</details>
