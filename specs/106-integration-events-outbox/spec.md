# Feature Specification: Integration events between modules with a transactional outbox

**Feature Branch**: `feature/106-integration-events-outbox`

**Created**: 2026-11-09

**Status**: Draft

**Input**: GitHub issue #106 — "Integration events between modules with a transactional outbox". Once each module owns its storage (#105 schemas, #107 one data context per module), a change in one module can no longer update another module in the same transaction. Modules talk through integration events stored in an outbox in the same transaction as the change, delivered by a background dispatcher, and de-duplicated by the receiver, so no event is lost or applied twice. The transport stays behind an abstraction: the database now, a broker later (#108). Practice project: extra design accepted for learning.

**Baseline**: `specs/001-birrapoint-mvp/` (US1–US14, FR-001–FR-065, SC-001–SC-013) and `specs/105-db-reset-module-schemas/` (US15–US17, FR-066–FR-075, SC-014–SC-018). Ids in this spec continue that sequence.

## Clarifications

### Session 2026-11-09

- Q: Acceptable delay between a change and its cross-module reaction? → A: ≤ 1 s, same as the realtime budget (FR-089, SC-020).
- Q: After a judge's email correction, the new address owns an entry at the judge's own table — what happens? → A: The judge stays seated, the entry is flagged not valid for Best of Show, and organizers get a realtime conflict alert (FR-082).
- Q: (PR review) Unknown event types after a rollback, dead events requeued by hand, and a correction racing the first PDF/ZIP build? → A: Unknown types stay pending without using attempts; inbox retention ≥ dead retention (default 30 d); the regeneration waits for running generation jobs (FR-083, FR-090).
- Q: After a correction on a `Finalized` competition, are participants emailed again? → A: No; PDF and ZIP are regenerated only (FR-083).
- Q: Do audit entries and job staging move to events? → A: No; they stay in the writing module's own transaction (Assumptions).
- Q: Outbox and inbox records grow forever — in scope? → A: Yes; a periodic purge with configurable retention (FR-090, SC-024). The open legacy-guard finding from #105 (newer migrations refused) is fixed here too, because this feature adds the first migration after the baseline (FR-091).

## User Scenarios & Testing *(mandatory)*

The actors are the organizer (who sees the effects of cross-module changes) and the developers and
operator who build and run BirraPoint. Judges see no change.

### User Story 18 - Cross-module effects happen exactly once, even after a crash (Priority: P1)

When an organizer makes a change in one module that another module must react to, the reaction
always happens — once — even if the application stops between the two. Today this covers:

- finalizing a competition (Organization) starts the results pipeline (Publication: PDFs, ZIP,
  emails);
- changing a table's judges (Judging) updates the competition-wide "not valid for Best of Show"
  flags (Organization, FR-018).

**Why this priority**: It is the core of the feature: the existing flows must keep working once
modules no longer share a transaction, and none of their effects may be lost or doubled.

**Independent Test**: Finalize a competition, stop the application before the results pipeline
starts, start it again and check that results are generated exactly once; repeat delivering the same
event several times and check that nothing is duplicated.

**Acceptance Scenarios**:

1. **Given** an `InEvaluation` competition ready to finalize, **When** the organizer finalizes it, **Then** the state change commits and the results pipeline starts afterwards, exactly once.
2. **Given** a state change that fails and rolls back, **When** the transaction ends, **Then** no event about it is ever delivered.
3. **Given** a committed change whose event was not yet delivered, **When** the application stops and starts again, **Then** the event is delivered after the restart.
4. **Given** an event already processed by a receiving module, **When** it is delivered again, **Then** the receiving module's data does not change a second time.
5. **Given** an organizer adds a judge who owns an entry to a table, **When** the change commits, **Then** that entry becomes "not valid for Best of Show" shortly after, and removing the judge (with no co-owner still engaged) clears it again.

---

### User Story 19 - Failed deliveries retry and end visibly (Priority: P2)

An operator can see when a cross-module reaction keeps failing: deliveries are retried with
increasing waits, and after the last attempt the event is parked as dead and reported in the logs,
without blocking the other events.

**Why this priority**: At-least-once delivery without a visible end state hides lost work; the
operator needs to find it.

**Independent Test**: Make one receiving reaction fail on purpose, publish an event, and check the
retry attempts, the final dead state and the log entry, while other events keep flowing.

**Acceptance Scenarios**:

1. **Given** a receiving reaction that fails, **When** its event is delivered, **Then** it is retried with increasing waits up to the attempt limit.
2. **Given** the attempt limit is reached, **When** the last attempt fails, **Then** the delivery is marked dead and an error log names the event type, event id and receiving module, with no personal data.
3. **Given** one dead delivery, **When** other events are published, **Then** they are delivered normally.

---

### User Story 20 - Correcting a judge's email re-checks conflicts of interest (Priority: P2)

When an organizer corrects a judge's email (FR-015 edge case), the conflict-of-interest and Best of
Show rules (FR-017/FR-018) are applied again against the new address, closing the recorded debt.

**Why this priority**: The REST contract already promises it; today the new address is never checked.

**Independent Test**: Seat a judge at a table, correct their email to the email of a participant who
owns an entry of the competition, and check the conflict and flag outcomes.

**Acceptance Scenarios**:

1. **Given** a judge whose new email matches the owner (participant or collaborator) of an entry in the competition, **When** the correction commits, **Then** that entry is flagged "not valid for Best of Show" while the judge takes part in the tasting (FR-018).
2. **Given** that entry is at a table where the judge is seated, **When** the correction commits, **Then** the judge stays seated, the entry is flagged, and the organizers receive a realtime conflict alert naming the judge, the table and the entries (FR-082).
3. **Given** the judge's old email was the reason an entry was flagged, **When** the correction commits and no other co-owner judge is engaged, **Then** the flag is cleared.

---

### User Story 21 - A correction after finalizing refreshes the results (Priority: P3)

When an organizer corrects an evaluation after the competition is `Finalized`, the published
results (score-sheet PDFs and the results ZIP) are regenerated, closing the recorded debt.

**Why this priority**: Corrections after finalizing are rare, but stale result files contradict the
audited correction.

**Independent Test**: Finalize a competition, wait for the results, correct one evaluation, and
check that the affected PDF and the ZIP reflect the corrected scores.

**Acceptance Scenarios**:

1. **Given** a `Finalized` competition whose results were generated, **When** the organizer corrects an evaluation, **Then** the affected entry's PDF and the results ZIP are regenerated with the corrected scores.
2. **Given** a correction on a competition that is not yet `Finalized`, **When** it commits, **Then** no result regeneration starts (results are first generated on finalize).
3. **Given** a regeneration, **When** it completes, **Then** no participant email is sent again; the organizer downloads the refreshed files (FR-083).

### Edge Cases

- Two events about the same competition are delivered out of order or concurrently: the receiving module's final state must be the same as if they arrived in order (FR-079).
- A receiving reaction succeeds but the application stops before recording it as processed: the redelivery must have no extra effect (FR-079).
- Several application instances run the dispatcher: each delivery is handled by one instance at a time (FR-078).
- An event refers to something deleted or no longer in a state that allows the reaction (e.g. the table was closed): the reaction does nothing and the delivery is completed, not retried forever.
- An event published to Judging carries any entrant field: forbidden (FR-085).
- The local stack is restarted with pending events in the outbox: they are delivered after the restart (US18 scenario 3).
- An unknown event type or version reaches a module (for example after rolling back to an older build): it stays pending and is retried at the longest backoff without using up attempts, with a warning log, so a later build that knows it still delivers it; it is never silently dropped.
- An event is redelivered after its inbox record was purged: the inbox retention is always longer than the outbox retention and any redelivery window, so de-duplication still holds (FR-090).
- The application is rolled back to an older build after this feature's migration ran, or a developer checks out an older branch: startup accepts a database that is only *ahead* of the build (FR-091).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-076**: Every module MUST publish the events other modules react to as integration events, declared in the publishing module's public contracts, through one publishing abstraction. Each event carries an id, a type, a version, the time it occurred and its payload.
- **FR-077**: An integration event MUST be stored in the publishing module's outbox in the same transaction as the state change that raised it; if the transaction rolls back, the event never exists.
- **FR-078**: A background dispatcher MUST deliver every stored event to every subscribing module at least once, after commit, with an atomic claim and a lease so that only one dispatcher instance handles a delivery at a time; deliveries whose lease expired (crash, restart) MUST be picked up again.
- **FR-079**: Each subscribing module MUST record the ids of the events it processed in its inbox, in the same transaction as its reaction, so a redelivered event has no effect. Reactions MUST converge to the same final state regardless of delivery order.
- **FR-080**: A failed delivery MUST be retried with increasing waits up to an attempt limit; after the last attempt it MUST be marked dead and logged at error level with event type, event id and subscriber, and without personal data. A dead delivery MUST NOT block other deliveries.
- **FR-081**: Every flow in which one module changes another module's data in the same transaction MUST move to integration events, each covered by an integration test. The full set is: `CompetitionFinalized` (Organization → Publication: start results generation, FR-036) and `TableJudgesChanged` (Judging → Organization: update Best of Show flags, FR-018). After this feature, no module writes another module's data.
- **FR-082**: When an organizer corrects a judge's email, Organization MUST publish `JudgeEmailChanged`. The conflict-of-interest and Best of Show rules (FR-017/FR-018) MUST be re-applied against the new address: entries owned by the new address are flagged while the judge takes part, entries flagged only because of the old address are cleared, and a judge seated at a table that holds an entry they now own stays seated, and the competition's organizers receive a realtime conflict alert naming the judge, the table and the entries so they can fix the table (the change is already committed, so it can no longer be rejected as in FR-017). The alert is a new realtime event added to the realtime contract.
- **FR-083**: When an organizer corrects an evaluation of a `Finalized` competition, Judging MUST publish `EvaluationCorrected`, and Publication MUST regenerate the affected entry's score-sheet PDF and the results ZIP. Participants are not emailed again; the regenerated files replace the downloadable ones, and the correction stays audit-logged. If the first score-sheet generation or ZIP build for the competition is still pending or running, the regeneration waits for it to finish, so the refreshed files always include the correction.
- **FR-084**: Realtime notifications MUST still be sent only after the transaction that caused them commits; a reaction in a receiving module sends its own notifications only after its own commit.
- **FR-085**: Integration events consumed by Judging MUST NOT carry entrant fields (name, participant, brewery, origin); contract tests MUST assert their absence.
- **FR-086**: Publishers and subscribers MUST NOT depend on how events travel; replacing the database-backed delivery with a message broker (#108) MUST NOT require changes to any publishing or subscribing code.
- **FR-087**: The reusable messaging capability (publishing abstraction, outbox, dispatcher, inbox) MUST have no reference to BirraPoint and MUST have its own automated tests.
- **FR-088**: Every integration event and its payload MUST be listed in the living contracts and the data model, and the decision (outbox over a broker for now, transport abstraction) MUST be recorded as an architecture decision.
- **FR-089**: The delay between a change's commit and its reaction in another module MUST stay within 1 s (in line with the realtime budget) under normal load: the dispatcher is woken right after commit, with periodic polling as a fallback.
- **FR-090**: Delivered and dead outbox records and processed inbox records MUST be purged periodically, each after a configurable retention period, in small batches that do not block delivery. The inbox retention MUST be longer than the outbox retention and at least as long as the dead retention (startup rejects a configuration where it is not), so a redelivered or manually requeued event is always still de-duplicated. Each purge run MUST log how many records it removed. Pending or in-flight outbox records are never purged.
- **FR-091**: The startup check for databases built from an old migration baseline (FR-070) MUST refuse only migrations older than the current baseline that the build does not know; migrations newer than the build (rollback to an older image, older branch) MUST NOT stop startup.

### Key Entities

- **Integration event**: a fact published by one module for others (id, type, version, occurred at, payload); declared in the publisher's public contracts.
- **Outbox entry**: an integration event stored by its publishing module in the same transaction as the change; pending until delivered.
- **Delivery**: one event to one subscribing module; has attempts, a next-attempt time, a lease and a final state (delivered or dead).
- **Inbox entry**: the record, per subscribing module, that an event id was processed; prevents a second effect. Kept for the inbox retention period, then purged (FR-090).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-019**: In crash and redelivery tests (application stopped between commit and delivery; each event delivered at least 3 times), 0 cross-module effects are lost and 0 are duplicated.
- **SC-020**: 95% of cross-module reactions are visible within the FR-089 target after the originating change commits, under normal load.
- **SC-021**: 100% of the flows listed in FR-081, FR-082 and FR-083 have an integration test, and 0 modules write another module's data.
- **SC-022**: A delivery that keeps failing is visible as dead in the logs within the configured retry window, and 100% of other deliveries keep flowing.
- **SC-023**: All existing automated suites pass; assertions change only where an effect becomes eventually consistent (the test waits for it instead of reading it at once).
- **SC-024**: After a purge run, 0 delivered or dead outbox records older than their retention and 0 inbox records older than theirs remain, while 100% of pending or in-flight outbox records are kept.

## Assumptions

- Audit entries and job-queue entries are cross-cutting building blocks, not modules: a module still writes its own audit entries and stages its own jobs in its own transaction; they do not become events.
- Cross-module reads that a request needs to answer synchronously (e.g. rejecting a conflicting table assignment at once, FR-017) stay synchronous calls through the modules' public contracts; only writes move to events.
- Delivery is at least once with de-duplication on receipt; there is no global ordering guarantee, so reactions are written to converge (FR-079).
- The database is the only transport in this feature; the broker (Dapr pub/sub with RabbitMQ) is #108. One data context per module is #107; this feature works with the current shared context.
- The attempt limit, waits and lease length follow the existing job queue's defaults unless the plan decides otherwise.
- Visibility of dead deliveries is logs only; no operator UI.
