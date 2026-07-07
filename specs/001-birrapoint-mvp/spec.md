# Feature Specification: BirraPoint MVP — Beer Competition Blind-Tasting Platform

**Feature Branch**: `001-birrapoint-mvp`

**Created**: 2026-07-06

**Status**: Draft

**Input**: User description: "Full MVP product specification for BirraPoint: RBAC actor model (Organizer, Judge, background sync agent), system flows F-01–F-09, business rules BR-01–BR-05, per-epic acceptance criteria, and MVP exclusions (tie-break rounds and Best of Show)."

## Clarifications

### Session 2026-07-06

- Q: Can judges start evaluating samples before someone at the table fixes the tasting order? → A: No — fixing the order is a mandatory precondition; evaluation is locked until a judge confirms "Fix Order".
- Q: Can a judge edit an evaluation sheet they already submitted, before the table closes? → A: No — sheets are locked on submit; the only reopening path for a judge is an open discrepancy alert involving that sheet.
- Q: At what granularity is the "Not valid for BOS" overlap computed? → A: Any table membership — the moment a judge with entries in the competition is assigned to any table, all of that judge's entries are flagged "Not valid for BOS".
- Q: What does each lifecycle state permit, and when can judges evaluate? → A: Explicit gates per state — Draft: organizer setup only, invisible to judges; Active: judges see assignments and can fix the order, sheets locked; In Evaluation: sheets unlock (organizer-triggered); Finalized: read-only plus dispatch.
- Q: What are the required fields for creating a competition in the wizard? → A: Minimal set — competition name, venue/location, start date, and end date are required; all other fields (description, logo, entry limits, registration window) are optional.

### Session 2026-07-07

- Q: Local orchestration — Docker Compose (constitution) or .NET Aspire (new infrastructure input)? → A: Aspire replaces Compose entirely as the single local orchestrator; constitution amendment required.
- Q: Which production PostgreSQL does the infrastructure provision? → A: PostgreSQL runs as a container inside the cloud container environment with persistent storage (not an externally managed database server).
- Q: Where does the identity provider run in production? → A: As a container in the same cloud environment, provisioned by the same single-command deployment as the rest of the system.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Secure Access with Role-Based Entry (Priority: P1)

Any user (organizer or judge) signs in through the platform's central identity portal. Judges who
received system-generated temporary credentials must set their own password before seeing any
competition data. After login, each user lands on the workspace matching their role.

**Why this priority**: Nothing else is usable without controlled access, and judge privacy depends
on forced credential rotation before any data exposure.

**Independent Test**: Provision one organizer and one judge (judge flagged for mandatory password
change); verify redirect-to-login, forced change, and role-based landing pages with no bypass.

**Acceptance Scenarios**:

1. **Given** an unauthenticated visitor, **When** they navigate to any address of the platform, **Then** they are redirected to the central login portal.
2. **Given** an authenticated organizer, **When** login completes, **Then** they land on the organizer dashboard.
3. **Given** an authenticated judge, **When** login completes, **Then** they land on their assigned-tables view.
4. **Given** a judge whose account is flagged as requiring a password change, **When** they authenticate, **Then** they must set a new password before any competition data is shown, **and** navigating directly to internal addresses cannot bypass this step.

---

### User Story 2 - Competition Creation Wizard with Drafts (Priority: P1)

An organizer creates a competition through a step-by-step wizard covering the base event data, and
can leave at any point by saving a draft to resume later.

**Why this priority**: The competition is the root record every other capability hangs from.

**Independent Test**: Walk the wizard, abandon mid-way with "Save draft", return, resume, and
complete the competition.

**Acceptance Scenarios**:

1. **Given** a wizard step with required fields incomplete, **When** the organizer tries to advance, **Then** the "Next" action stays disabled until the current step is valid.
2. **Given** an organizer mid-wizard, **When** they choose "Save draft", **Then** the competition is stored in `Draft` state and the organizer can exit without any data-integrity penalty.
3. **Given** a competition in `Draft`, **When** the organizer reopens it, **Then** the wizard resumes with all previously entered data intact.

---

### User Story 3 - Beer Entry Import with In-Flow Correction (Priority: P1)

An organizer bulk-loads registered beers from an `.xlsx` file. Every row is validated against the
official BJCP 2021 master style catalog. Rows with unrecognized styles do not abort the load;
instead a "Mapping & Correction" screen lets the organizer fix them against the catalog before
consolidating the import.

**Why this priority**: Competitions typically have hundreds of entries; manual entry is not viable
and import failure at the venue is unacceptable.

**Independent Test**: Import a file mixing valid rows and rows with misspelled/unknown styles;
resolve every failure in the correction screen and consolidate.

**Acceptance Scenarios**:

1. **Given** a well-formed file whose rows all match the catalog, **When** it is uploaded, **Then** all entries are imported and each is linked to its official style.
2. **Given** a file containing rows whose style code/name does not exactly match the BJCP 2021 catalog, **When** validation runs row by row, **Then** the load is not aborted and the failing rows appear in a correction screen where the organizer assigns the right style from a searchable catalog list.
3. **Given** unresolved failing rows, **When** the organizer attempts to consolidate the import, **Then** consolidation is blocked until every failing row is either corrected or explicitly excluded.
4. **Given** a consolidated import, **When** entries are persisted, **Then** each entry receives a unique alphanumeric blind code.

---

### User Story 4 - Judge Registration and Automatic Invitations (Priority: P1)

An organizer pastes a list of email addresses; the system creates passive judge profiles with
temporary credentials and dispatches invitation emails automatically.

**Why this priority**: Judges cannot participate without accounts, and manual account creation
does not scale to a full panel.

**Independent Test**: Submit a list of emails (including a duplicate); verify profiles, dispatched
invitations, and deduplication reporting.

**Acceptance Scenarios**:

1. **Given** a list of judge emails, **When** the organizer submits it, **Then** a passive profile is created for each address and an invitation with temporary credentials is emailed automatically.
2. **Given** a list containing duplicates or already-registered addresses, **When** it is submitted, **Then** duplicates are not created and the organizer is shown what was skipped and why.
3. **Given** a newly invited judge, **When** they first sign in, **Then** the forced password change of User Story 1 applies.

---

### User Story 5 - Table Setup with Conflict-of-Interest Protection (Priority: P1)

An organizer creates tasting tables and assigns each one a group of judges and a set of beers. The
system blocks assignments where a judge would evaluate a beer they own or collaborated on, and
flags all entries owned by judging participants as ineligible for Best of Show.

**Why this priority**: Table assignments define the entire judging workload, and undetected
conflicts of interest invalidate results.

**Independent Test**: Assign judges and beers to a table including one judge who owns an assigned
beer; verify rejection. Repeat with a judge who owns entries elsewhere in the competition; verify
all of that judge's entries are flagged "Not valid for BOS".

**Acceptance Scenarios**:

1. **Given** a table with beers assigned, **When** the organizer assigns a judge who is owner or collaborator of any of those beers, **Then** the assignment is rejected as a whole with a conflict error identifying the judge and the entries involved (no partial save).
2. **Given** valid selections, **When** the organizer saves the table, **Then** the table persists with its N judges and M beers.
3. **Given** a judge who has entries in the competition, **When** that judge is assigned to any table (without a direct own-beer collision), **Then** all of the judge's entries in the competition are automatically flagged "Not valid for BOS" and the organizer is warned.

---

### User Story 6 - Blind Table Dynamics: Shared Fixed Order (Priority: P1)

Judges see their table's samples identified only by blind code and style. One judge arranges the
tasting sequence and fixes it; every table member is then locked to that exact order and must
evaluate strictly in sequence.

**Why this priority**: Blind, uniformly ordered evaluation is the core integrity guarantee of the
whole product.

**Independent Test**: With two judge sessions on one table, reorder and fix the order from one;
verify the other session reorders immediately, ordering is frozen for all, and out-of-sequence
evaluation is impossible.

**Acceptance Scenarios**:

1. **Given** a judge viewing their table's sample list, **Then** each sample shows only its blind code and style/category — never brewer name, beer name, brewery, origin, or any registration metadata.
2. **Given** the order not yet fixed, **When** a judge rearranges samples (by drag & drop or an equivalent keyboard-accessible interaction) and confirms "Fix Order", **Then** all table members' lists reorder to that sequence within 1 second, further reordering is disabled for everyone, and an indicator shows "Order fixed by Judge X".
3. **Given** a fixed order, **When** any judge attempts to open a sample out of sequence, **Then** the system prevents it — the next sample only unlocks when the current one is submitted.
4. **Given** two judges fixing the order at nearly the same time, **When** both confirm, **Then** exactly one sequence wins and the other judge is notified and refreshed to the winning order.
5. **Given** a table whose order has not been fixed yet, **When** any judge attempts to open an evaluation sheet, **Then** the system prevents it and indicates that the tasting order must be fixed first.

---

### User Story 7 - Offline-First Validated Evaluation Sheet (Priority: P1)

A judge fills a five-section evaluation sheet whose scores are capped per section and totaled
automatically. Everything typed is preserved on the device instantly, works without connectivity,
and syncs silently — exactly once — when the network returns.

**Why this priority**: Score capture is the product's reason to exist, and venue connectivity
cannot be assumed.

**Independent Test**: Fill a sheet in airplane mode, restart the app, verify data intact, restore
connectivity, and verify exactly one evaluation reaches the server.

**Acceptance Scenarios**:

1. **Given** the evaluation form, **Then** its sections enforce these maxima and nothing beyond them can be entered: Aroma 12, Appearance 3, Flavor 20, Mouthfeel 5, Overall Impression 10; the total is the exact computed sum (max 50) and cannot be edited manually.
2. **Given** any section with a descriptive comment shorter than 20 characters, **When** the judge attempts to submit, **Then** submission is blocked and the incomplete sections are indicated.
3. **Given** the judge typing or changing focus, **Then** the draft is persisted locally within 300 ms of each change.
4. **Given** lost connectivity, **Then** a discreet badge shows "Offline mode — data protected locally" and the judge can keep working normally.
5. **Given** connectivity returns, **Then** pending evaluations sync in the background with no user action, **and** retries after network failures never produce duplicate evaluations — at most one evaluation exists per judge and sample.

---

### User Story 8 - Table Closing and Score Immutability (Priority: P1)

When every evaluation at a table is complete, a judge closes the table, permanently locking all
its evaluations against modification by any judge.

**Why this priority**: Immutability after close is what makes the digital scores trustworthy as
official results.

**Independent Test**: Complete all sheets at a table, close it, then attempt edits as each judge
(must fail) and as organizer (allowed, logged).

**Acceptance Scenarios**:

1. **Given** a table where all evaluations are complete and no discrepancy alert is unresolved, **When** a judge confirms "Close Table", **Then** all the table's evaluations become permanently read-only for every judge.
2. **Given** a closed table, **When** any user without the organizer role attempts to create, modify, or delete one of its evaluations (including delayed offline syncs), **Then** the operation is rejected.
3. **Given** a closed table, **When** the organizer performs a correction, **Then** the change is applied and recorded with who/when/what for audit.
4. **Given** a table with incomplete evaluations, **When** a judge attempts to close it, **Then** closing is blocked and the missing evaluations are listed.

---

### User Story 9 - Live Monitoring Dashboard with Audit (Priority: P2)

The organizer watches per-table progress update in real time as judges complete evaluations, and
can drill into any evaluated sample to read the judges' notes without being able to alter them.

**Why this priority**: Live visibility lets the organizer run the event; it depends on evaluation
capture (P1) existing first.

**Independent Test**: With the dashboard open, submit an evaluation from a judge session and watch
the table's progress update without reloading; open the sample's notes read-only.

**Acceptance Scenarios**:

1. **Given** the organizer dashboard open, **When** a judge completes an evaluation, **Then** the corresponding table's progress indicator updates within 1 second, smoothly, with no flicker or full page reload.
2. **Given** an evaluated sample on the dashboard, **When** the organizer selects it, **Then** all its submitted evaluations open in read-only mode.

---

### User Story 10 - Event Closing with Automated Results Dispatch (Priority: P2)

When the competition ends, the organizer closes the event; the system generates a PDF for every
evaluation sheet in the background, packages them as a structured ZIP for download, and emails
each participant their own sheets automatically.

**Why this priority**: This delivers the competition's final product to participants, but only
matters once evaluations exist.

**Independent Test**: Close an event with completed tables; verify the platform stays responsive
during generation, the ZIP structure is correct, and every participant receives their PDFs.

**Acceptance Scenarios**:

1. **Given** a competition ready to finish, **When** the organizer closes the event, **Then** its state becomes `Finalized` and PDF generation for all evaluation sheets starts in the background while the platform remains responsive.
2. **Given** generation is complete, **When** the organizer downloads the results archive, **Then** the ZIP follows the hierarchy `/CompetitionName/ParticipantID/Style_BlindCode.pdf`.
3. **Given** generation is complete, **Then** each participant automatically receives an email with their own evaluation sheets attached as PDFs.
4. **Given** one or more emails fail to deliver, **Then** the organizer sees per-recipient delivery status and can retry the failed ones.

---

### User Story 11 - Discrepancy Consensus Alert (Priority: P2)

When a judge submits a total that deviates more than 7 points from another judge's total for the
same sample, both submissions are held as provisional and a discrepancy alert forces the judges to
reconcile before the table can close.

**Why this priority**: Score consensus is a recognized quality standard for competition judging,
but the core capture loop works without it.

**Independent Test**: Have two judges score the same sample 15 points apart; verify the alert
opens, adjustments are possible, and table close stays blocked until totals converge within 7.

**Acceptance Scenarios**:

1. **Given** at least one already-submitted total for a sample, **When** another judge of the same table submits a total differing by more than 7 points from any of them, **Then** the submission is held as provisional and a discrepancy alert opens for the judges involved.
2. **Given** an open discrepancy alert, **When** the involved judges adjust their scores so that all totals for the sample are within 7 points of each other, **Then** the evaluations can be finalized and the alert closes.
3. **Given** an unresolved discrepancy on any sample, **When** a judge attempts to close the table, **Then** closing is blocked and the conflicting samples are listed.
4. **Given** a sample evaluated by only one judge so far, **When** that judge submits, **Then** no alert is raised.

---

### User Story 12 - Live Judge Removal for Conflict of Interest (Priority: P3)

If a conflict of interest surfaces during the live event (a judge recognizes a beer or
self-reports), the organizer removes the judge from the table directly from the live dashboard;
the judge's device reflects the removal immediately and all their editing rights over that
table's samples are revoked.

**Why this priority**: A safety valve for the exceptional case; the primary defense is the
assignment-time validation of User Story 5.

**Independent Test**: With a judge session open on a table, remove that judge from the dashboard;
verify their panel ejects them instantly and any further edit or sync attempt for those samples is
rejected.

**Acceptance Scenarios**:

1. **Given** a live table, **When** the organizer removes a judge from it, **Then** the judge's device updates immediately: the table disappears from their workspace and editing rights over its samples are revoked at once (including pending offline syncs, which are rejected and reported).
2. **Given** a removed judge who had already submitted evaluations for that table, **Then** those submitted evaluations remain valid — the conflict is mitigated by the assignment-time controls and the "Not valid for BOS" flag of User Story 5, not by voiding third-party scores.
3. **Given** a removal, **Then** the action is recorded (who removed whom, from which table, when) for audit.

---

### Edge Cases

- A judge goes offline before the table order is fixed: on reconnection their list reorders to the fixed sequence; samples already submitted remain valid regardless of their position in the new order.
- A judge's offline sync arrives after the table was closed: the sync is rejected (immutability), the judge is notified, and the held data is surfaced to the organizer for manual resolution rather than silently discarded.
- Two judges press "Fix Order" almost simultaneously: exactly one wins; the loser is refreshed to the winning order (User Story 6, scenario 4).
- The imported file is empty, malformed, or not a spreadsheet: the upload is rejected with a clear reason before row validation starts.
- An invitation email address is invalid or bounces: the profile still exists; the organizer sees the delivery failure and can correct the address and resend.
- Removing a judge leaves a table with no judges or below the minimum needed: the organizer is warned that the table cannot produce results until staffed.
- A discrepancy involves three or more judges: the alert includes every judge whose total is more than 7 points from any other total for that sample.
- A discrepancy is detected from a late offline sync while an involved judge is offline: the alert is shown to each involved judge as soon as they are next online; the table remains blocked from closing until resolved (per FR-031/FR-032).
- Local device storage is unavailable or full: the judge is warned immediately that offline protection is compromised instead of failing silently.
- The organizer closes the event while a table is still open: closing is blocked until every table is closed.

## Requirements *(mandatory)*

### Functional Requirements

**Identity & Access**

- **FR-001**: The system MUST redirect unauthenticated users to the central login portal before showing any content.
- **FR-002**: After login the system MUST route users to the workspace matching their role: organizers to the organizer dashboard, judges to their assigned-tables view.
- **FR-003**: Accounts flagged as requiring a password change MUST be forced to set a new password before any competition data is displayed; direct navigation MUST NOT bypass this step.
- **FR-004**: Judges MUST be able to view and edit only the samples assigned to their table(s); removing an assignment revokes that access immediately.
- **FR-005**: Only organizers can: manage the competition lifecycle, import entries, register judges, manage tables, view audit panels, close the event, and trigger dispatch.

**Competition Lifecycle**

- **FR-006**: A competition MUST move through the states `Draft` → `Active` → `In Evaluation` → `Finalized`; transitions are organizer-only, forward-only, and skip-free. Each state gates capabilities:
  - `Draft`: organizer setup only (wizard, entry import, judge registration, tables); the competition is invisible to judges.
  - `Active`: judges see their table assignments and the tasting order can be fixed; evaluation sheets remain locked; the organizer can still adjust setup (imports, tables, judges).
  - `In Evaluation`: evaluation sheets unlock (still subject to the fixed-order precondition of FR-022); entry imports and wizard edits are no longer allowed; live monitoring and judge removal apply.
  - `Finalized`: everything becomes read-only and results generation/dispatch runs (FR-036, FR-040, FR-041).
- **FR-007**: The creation wizard MUST validate the required fields of each step and keep the "Next" action disabled until the current step is valid. Required fields: competition name, venue/location, start date, and end date (end date must not precede start date); all other fields (description, logo, entry limit, registration window start/end) are optional.
- **FR-008**: The wizard MUST allow saving as `Draft` at any step and resuming later with all entered data intact.

**Entry Import**

- **FR-009**: The system MUST accept `.xlsx` files and validate them row by row against the expected structure.
- **FR-010**: Each row's style MUST match the official BJCP 2021 catalog by exact code or exact name; non-matching rows MUST be routed to a correction screen instead of aborting the load.
- **FR-011**: The correction screen MUST let the organizer resolve each failing row by picking a style from the catalog or explicitly excluding the row; consolidation MUST be blocked while unresolved rows remain.
- **FR-012**: The official BJCP 2021 style catalog MUST be preloaded in the system as read-only master data.
- **FR-013**: On consolidation, every imported entry MUST receive a unique alphanumeric blind code.

**Judge Provisioning**

- **FR-014**: The system MUST create passive judge profiles from a submitted email list and automatically dispatch invitation emails with temporary credentials.
- **FR-015**: Duplicate addresses (within the list or already registered) MUST NOT create duplicate profiles and MUST be reported to the organizer.

**Tables & Conflict of Interest**

- **FR-016**: Organizers MUST be able to create tables and assign each one a set of judges and a set of beers.
- **FR-017**: An assignment placing a judge at a table containing a beer they own or collaborated on MUST be rejected atomically with a conflict error identifying the judge and entries involved.
- **FR-018**: When a judge who has entries in the competition is assigned to any table, the system MUST flag all of that judge's entries "Not valid for BOS", warn the organizer, and carry the flag into audit views and result exports. The flag is lifted only if the judge loses all table assignments before submitting any evaluation; once the judge has submitted at least one evaluation, the flag is permanent for that competition.

**Blind Tasting Dynamics**

- **FR-019**: Judge-facing views MUST identify samples exclusively by blind code and style/category; brewer name, beer name, brewery, origin, and registration metadata MUST never appear in any judge-facing screen, list, message, or notification.
- **FR-020**: Judges MUST be able to reorder their table's samples via drag & drop and via an equivalent keyboard-accessible interaction.
- **FR-021**: Confirming "Fix Order" MUST persist the sequence for the table, propagate it to all table members within 1 second, disable further reordering for everyone, and display who fixed it.
- **FR-022**: Fixing the order is a mandatory precondition for evaluation: no evaluation sheet at a table can be opened until the table's order is fixed. Once fixed, evaluation MUST be strictly sequential: a sample only becomes available when the previous one has been submitted.

**Evaluation Sheet**

- **FR-023**: The evaluation sheet MUST have five scored sections with hard caps — Aroma 12, Appearance 3, Flavor 20, Mouthfeel 5, Overall Impression 10 — enforced at input so no section can exceed its maximum.
- **FR-024**: The total score MUST be the exact computed sum of the five sections (maximum 50) and MUST NOT be manually editable.
- **FR-025**: Each section MUST require a descriptive comment of at least 20 characters before the sheet can be submitted.
- **FR-026**: Drafts MUST persist on the judge's device within 300 ms of every keystroke or focus change.
- **FR-027**: On connectivity loss the interface MUST show a discreet "Offline mode — data protected locally" indicator while remaining fully usable.
- **FR-028**: Pending evaluations MUST sync automatically and silently when connectivity returns.
- **FR-029**: Sync retries MUST never create duplicates: the system MUST recognize resubmissions of the same evaluation (same competition, table, judge, and sample) and store at most one evaluation per judge and sample.
- **FR-030**: Once submitted, an evaluation sheet becomes read-only for its judge; the only path for a judge to modify a submitted sheet is an open discrepancy alert involving it (FR-031). Organizer corrections follow FR-035.

**Discrepancy Consensus**

- **FR-031**: On submission, the system MUST compare the sheet's total with every already-submitted total for the same sample at the same table; if any difference exceeds 7 points, the submission MUST be held as provisional and a discrepancy alert opened for the judges involved.
- **FR-032**: A table MUST NOT close while any of its samples has an unresolved discrepancy; resolution means all submitted totals for the sample are within 7 points of each other.

**Table & Event Closing**

- **FR-033**: Any judge of a table MUST be able to trigger "Close Table" once all its evaluations are complete and no discrepancy is unresolved; closing is permanent.
- **FR-034**: After close, the table's evaluations become read-only: create/modify/delete attempts by any user without the organizer role MUST be rejected, including late offline syncs.
- **FR-035**: Organizer corrections after close MUST be recorded with author, timestamp, and change for audit.
- **FR-036**: Closing the event MUST require all tables closed, set the competition to `Finalized`, and trigger background generation of one PDF per evaluation sheet without degrading platform responsiveness.

**Monitoring & Dispatch**

- **FR-037**: The organizer dashboard MUST reflect each completed evaluation in the corresponding table's progress within 1 second, without page reloads or flicker.
- **FR-038**: The organizer MUST be able to open any evaluated sample from the dashboard and read all its submitted evaluations in read-only mode.
- **FR-039**: The organizer MUST be able to remove a judge from a table during the live event; the judge's device MUST reflect the removal immediately and all editing rights over that table's samples MUST be revoked at once, with the action recorded for audit. Evaluations the judge had already submitted remain valid.
- **FR-040**: The results archive MUST be a downloadable ZIP structured `/CompetitionName/ParticipantID/Style_BlindCode.pdf`.
- **FR-041**: On event close, each participant MUST automatically receive an email with their own evaluation sheets attached as PDFs; per-recipient delivery status MUST be visible to the organizer with the ability to retry failures.
- **FR-042**: Once a table closes, the system MUST compute each sample's consolidated score as the arithmetic mean of its submitted totals and expose it to the organizer.

**Operations & Deployment** *(added 2026-07-07)*

- **FR-043**: Every runtime component (frontend, backend, identity provider, database) MUST run as an isolated container built reproducibly from source; images MUST NOT contain secrets or environment-specific configuration.
- **FR-044**: A developer MUST be able to start the complete system locally — database, identity provider, backend, frontend, and mail sink — with a single command, through a single orchestrator whose topology mirrors production.
- **FR-045**: All cloud infrastructure MUST be defined declaratively (infrastructure as code); a fresh environment MUST be provisionable and the full system deployable end to end with a single command and zero manual configuration steps.
- **FR-046**: The production environment MUST host the frontend (publicly reachable), the backend API, and the identity provider (reachable for login flows) as separately deployable containers inside one secured environment boundary; all configuration and secrets are injected at deploy time (environment variables / secret store).
- **FR-047**: The production database MUST run as a container inside the same environment boundary, with persistent storage that survives container restarts and redeployments, a scheduled automated backup/export, and a documented restore procedure.
- **FR-048**: Every service MUST expose health-check endpoints and emit standardized telemetry (logs, metrics, traces) in both local and cloud environments.

### Key Entities

- **Competition**: root event record; required: name, venue/location, start date, end date; optional wizard data (description, logo, entry limit, registration window start/end); lifecycle state (`Draft`, `Active`, `In Evaluation`, `Finalized`) with per-state capability gates (FR-006).
- **Style (BJCP 2021 Catalog)**: read-only master data; official code, name, category. Preloaded; target of import matching.
- **Beer Entry (Sample)**: a registered beer; participant (owner), optional collaborators, style, blind code, "Not valid for BOS" flag. Judge-facing views expose only blind code + style.
- **Participant (Brewer)**: entrant identity and contact email; owner of entries; recipient of result PDFs. Never visible to judges.
- **Judge**: platform user with judge role; profile created passively from email; may also be a participant (drives conflict-of-interest rules).
- **Table (Mesa)**: judging unit binding a set of judges and a set of samples; holds the fixed tasting sequence and its open/closed state.
- **Tasting Sequence**: the per-table sample order; unset until a judge fixes it, then immutable and shared by all table members.
- **Evaluation Sheet**: one judge's assessment of one sample; five section scores with caps, per-section comments (≥20 chars), computed total (≤50), lifecycle: draft → submitted (locked for its judge) → provisional while a discrepancy alert is open → read-only for all judges after table close.
- **Discrepancy Alert**: raised when totals for a sample diverge by more than 7 points; tracks involved judges and resolution state; blocks table close while open.
- **Invitation**: dispatched email with temporary credentials; tracks delivery status.
- **Results Package**: generated PDFs per evaluation sheet, ZIP archive, and per-participant email dispatch status.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Zero brewer-identifying data appears in any judge-facing view, message, or notification across the entire evaluation flow (verified by audit of every judge-facing screen).
- **SC-002**: 100% of evaluations captured offline reach the server exactly once after connectivity returns — no losses and no duplicates — under repeated disconnect/reconnect cycles.
- **SC-003**: Everything a judge types is preserved within 300 ms and survives an app restart or page reload mid-evaluation.
- **SC-004**: A fixed tasting order is visible to all connected table members within 1 second of confirmation.
- **SC-005**: The organizer dashboard reflects a completed evaluation within 1 second, without page reloads.
- **SC-006**: An import of 500 entries with up to 20% style errors can be fully resolved and consolidated in a single session, without restarting the upload.
- **SC-007**: After a table closes, zero score modifications by judges succeed, including delayed offline submissions.
- **SC-008**: 100% of participants receive their evaluation PDFs by email after event close with no manual per-participant action; any failure is visible to the organizer and retryable.
- **SC-009**: A judge can complete and submit an evaluation sheet end to end using only the keyboard, and all judge-facing flows satisfy WCAG 2.1 AA.
- **SC-010**: A first-time judge completes their first evaluation sheet in under 10 minutes without external help or training material, verified in usability testing with at least 5 judges.
- **SC-011**: A fresh cloud environment is provisioned and the full system deployed by executing a single command, with zero manual configuration steps; a fresh local environment starts with a single command.

## Assumptions

- Any judge of a table can rearrange and fix the tasting order; the platform has no separate "table lead" role in the MVP — whoever fixes the order acts as the de facto coordinator and is identified in the UI (derived from backlog US-3.1.2; the actor model's "lead/coordinator" is interpreted as this de facto role).
- Conflict-of-interest detection matches judges to entries via the participant's registered email and declared collaborators from the import file; the import structure therefore includes at least: participant name, participant email, beer name, style (code or name), and optional collaborator emails.
- Blind codes are generated by the system at import consolidation, unique per competition.
- Best of Show and tie-break rounds stay manual and out of scope, but the "Not valid for BOS" flag is recorded and exported so the manual BOS process can honor it.
- Discrepancy resolution requires actual convergence to within 7 points; the MVP offers no organizer override for deadlocks.
- The consolidated score per sample is the arithmetic mean of submitted totals, computed once the table closes.
- Expected scale per event: up to ~500 entries, ~100 judges, ~25 tables; the platform may host multiple competitions but a single live event at a time is the design center.
- Judges use their own mobile devices at venues with intermittent connectivity; offline capability is an architectural property of the evaluation flow, not a feature toggle.
- Authentication, credential lifecycle, and forced password change are delegated to the platform's central identity provider as fixed by the project's approved technology definition.
- A transactional email delivery service is available for invitations and result dispatch (external dependency); in production it is configured via environment variables.
- The concrete containerization, local orchestration, IaC tooling, and cloud target for FR-043–FR-048 are fixed by the project's approved technology definition (constitution, Technology & Architecture Constraints — amended for this feature) rather than restated here.
- Because the production database runs in-environment (FR-047) instead of on a managed database service, backup/export scheduling and restore verification are an explicit operational responsibility of the deployment, not the platform.
- Excluding a row during import correction removes it from the import only; it is reported in the import summary.

## Out of Scope (MVP)

- **Tie-break rounds and Best of Show (BOS)**: algorithmic or visual handling of ties and the final BOS round are excluded. These continue on paper, coordinated in person by organizers and head judges. The platform's only contribution is the recorded "Not valid for BOS" eligibility flag on affected entries.
