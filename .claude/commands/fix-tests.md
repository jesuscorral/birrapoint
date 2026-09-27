---
description: Loop until the backend and frontend unit/integration suites pass
argument-hint: [backend|frontend|all]
---

Scope: $ARGUMENTS (empty means all).

Suites:
- backend: `dotnet test backend/tests/BirraPoint.Api.UnitTests` and
  `dotnet test backend/tests/BirraPoint.Api.IntegrationTests` (needs Docker running)
- frontend: `cd frontend && npx jest`

Loop: run the suite, and for each failure read the test and the code under test (use
`codegraph_explore`), find the real cause, fix it, re-run only the failing tests
(`--filter "FullyQualifiedName~Name"` / `npx jest <path>`), then the full suite. Never skip,
delete or weaken a test. If a fix needs a product or spec decision, stop and ask. If Docker is not
running, say so instead of skipping the integration suite silently.
