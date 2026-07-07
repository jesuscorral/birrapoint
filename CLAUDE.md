# Project Instructions

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Methodology
This project uses Spec-Driven Development.

The source of truth is:
1. `.specify/memory/constitution.md`
2. `specs/**/spec.md`
3. `specs/**/plan.md`
4. `specs/**/tasks.md`
5. Tests and acceptance criteria

Do no implement new functionaly unless there is an approved spec, plan and task list.

## Workflow 

For every new Feature:
1. Create or update the specification
2. Clarify ambiguos requirements.
3. Create a technical plan.
4. Generate tasks.
5. Review tasks before implementation.
6. Implement task by task.
7. Run tests and quality checks.
8. Update docs if behavior changes.

## Coding Standards
- Prefer small, reviewable changes.
- Keep business login sperated from UI and infrastructure.
- Add tests before alongside implementation.
- Do not introduce unnecessary dependencies.
- Explain trade-offs for architecture decisions.
- Do not silently change product requirements.

## Architecture
This project is a Modular Monolith with Vertical Slice Architecture and Domain-Drive Design.
The system must be organized by business modules, not by generic technical layers.
Correct:
```text
src/modules/bookings/
src/modules/payments/
src/modules/users/
src/shared/kernel
```

Incorrect:
```text
src/controllers
src/services
src/repositories
src/models
```
## Testing 
Before considering work complete, run:
- Unit tests
- Integration tests when appiclabe
- Lint/types checks
- Build command

If a test cannot be run, explain why and what was verified instead.

## Security
- Never commit secrets
- Use environment variables for credentials
- Validate inputs at API boundaries
- Avoid logging sensitive data.

## Git
- work on feature branches
- Use small commits
- Commit generated spec artifacts together with the implementation

## Project Status

**Greenfield — no application code exists yet.** The repository contains only planning documents (`Docs/`) and GitHub Spec Kit scaffolding (`.specify/`, `.claude/skills/`, `.github/prompts/`). There are no build, lint, or test commands until the first code is scaffolded; add them here when that happens.

BirraPoint is a planned PWA for managing beer competitions and blind tastings (catas a ciegas), with offline support and real-time monitoring. The planning docs in `Docs/` are written in Spanish.

## Spec-Driven Workflow (Spec Kit)

Development follows the Spec Kit workflow (v0.12.4, Claude integration). Feature work goes through these skills, in order:

1. `/speckit-constitution` — maintain project principles (`.specify/memory/constitution.md`, ratified — currently v1.1.0, stack pinned to Angular 17+/.NET 10 LTS)
2. `/speckit-specify` — create a feature spec from a natural-language description
3. `/speckit-clarify` — resolve underspecified areas in the spec
4. `/speckit-plan` — generate the implementation plan and design artifacts
5. `/speckit-tasks` — generate a dependency-ordered `tasks.md`
6. `/speckit-implement` — execute the tasks

Supporting skills: `/speckit-analyze` (cross-artifact consistency check), `/speckit-checklist`, `/speckit-converge` (diff codebase vs. spec and append remaining tasks), `/speckit-taskstoissues` (GitHub issues).

- Helper scripts are **PowerShell only**: `.specify/scripts/powershell/` (`create-new-feature.ps1`, `setup-plan.ps1`, `setup-tasks.ps1`, `check-prerequisites.ps1`). Feature numbering is sequential.
- `.specify/extensions.yml` defines an `agent-context` hook that runs `speckit.agent-context.update` after specify and plan steps.
- Active feature: `specs/001-birrapoint-mvp/` (spec + clarifications, plan, research, data-model, contracts/, quickstart; tasks pending). `.specify/feature.json` points downstream commands at it.
