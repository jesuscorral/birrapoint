---
description: Loop until all tests pass
---

Run both suites: `dotnet test Birrapoint.sln --nologo` and `npm test --prefix src/birrapoint-web -- --watch=false`.

Loop: for each failure, read the failing test and the code under test, determine the real cause, fix it, re-run. Never skip, delete, or weaken a test to make it pass. Continue until both suites are fully green. If a failure requires a product decision, stop and ask me.
