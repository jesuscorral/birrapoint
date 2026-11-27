# Quickstart: Integration events between modules

Validation scenarios for [spec.md](./spec.md). Prerequisites: the CLAUDE.md §Commands toolchain
and Docker. This feature adds a migration, so a local database built by #105 upgrades in place and
needs no reset.

## Automated

```bash
dotnet test tests/BuildingBlocks/BuildingBlocks.Messaging.Tests      # block: outbox, dispatcher, inbox, retry/dead, wake-up
dotnet test tests/BirraPoint.Api.UnitTests --filter "FullyQualifiedName~LegacyDatabaseGuard"
dotnet test tests/BirraPoint.Api.IntegrationTests --filter "FullyQualifiedName~IntegrationEvents"
dotnet test tests/BirraPoint.Api.IntegrationTests                    # whole suite (SC-023)
```

## §1 Finalize then crash (US18, SC-019)

1. Covered by an integration test. The dispatcher is disabled, the competition is finalized, and
   the test checks: one `organization.OutboxMessages` row `Pending` and no `GeneratePdfs` job.
2. The test host restarts with the dispatcher on. Expected: the row becomes `Delivered`, exactly
   one `GeneratePdfs` job exists, and one `publication.InboxMessages` row is present.
3. The same event is forced back to `Pending` and redelivered 3 times. Expected: still one job.

## §2 Table judges and BOS flags (US18 scenario 5, SC-020)

Local stack: open a competition in `Active` and add to a table a judge whose email owns an entry.
Expected: within 1 s the entry shows "not valid for BOS" in the tables screen (`GET .../tables`).
Remove the judge: the flag clears. The latency test asserts p95 ≤ 1 s over 20 runs with the
safety-net poll at 1 h.

## §3 Failure and dead letter (US19, SC-022)

Covered by a block test: a subscriber throws every time. Expected:
- 5 attempts with growing `NextAttemptAt`;
- status `Dead`;
- one error log naming type, event id and subscriber, with no payload;
- an event published afterwards is still `Delivered`.

## §4 Judge email correction (US20)

Local stack: in a `Draft`/`Active` competition, seat judge J at a table that holds entry E. Change
J's email to E's participant email with `PUT /competitions/{id}/judges/{judgeId}`. Expected:
- E is `notValidForBos` straight after the response;
- the competition monitor and the tables screen show the `JudgeConflictDetected` banner naming J
  and the table;
- J is still seated.

Change the email back: the flag clears and no alert shows.

## §5 Correction after finalizing (US21)

Local stack: finalize a competition and wait for dispatch `Completed`. Correct one evaluation of
entry E. Expected:
- E's PDF (`GeneratedAt` changes) and the results ZIP show the corrected total;
- Mailpit receives no new result email.

Correcting before finalizing starts no `RefreshResults` job.

## §6 Rollback safety (FR-077)

Covered by an integration test: a finalize whose transaction fails leaves no outbox row and no job.

## §7 Retention purge (FR-090, SC-024)

Covered by a block test with a fake clock and retentions of seconds. Seed rows: `Delivered` and `Dead` outbox rows past retention, a `Pending` and a `Delivering` row, inbox rows on both sides of the retention. After one purge pass: only the old delivered/dead/inbox rows are gone, and the log reports the counts. A batch size of 2 with 5 old rows still removes all 5. Startup fails when `InboxRetention <= OutboxRetention` or `InboxRetention < DeadRetention`.

## §8 Database ahead of the build (FR-091)

Unit test: applied = baseline + a newer unknown id ⇒ allowed; applied = an unknown id older than the baseline ⇒ refused (unchanged). Manual: run this branch (migration applied), switch to `main`, start the stack: the API starts with no reset prompt.
