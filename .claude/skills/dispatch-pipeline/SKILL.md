---
name: "dispatch-pipeline"
description: "DispatchJob queue conventions shared by judge invitations (US4) and results dispatch (US10) — idempotent resume, QuestPDF/ZIP/email chaining (R-06/R-10/R-14). Load before touching Common/Jobs, SendInvitation, or the PDF/ZIP/email pipeline."
user-invocable: true
disable-model-invocation: false
---

# DispatchJob queue

Infra (T016, shared by both stories): `DispatchJob` is DB-persisted, processed by a hosted
`BackgroundService` woken via Channels — no Hangfire, no external broker (R-06). Jobs must resume
correctly on API restart by re-scanning pending/in-flight rows, not just by staying alive in
memory.

Idempotency: every job handler must be safe to run twice (retry after partial failure). Track
status/attempts/lastError on the job row itself (`Invitation.Status`/`DispatchJob` fields) — never
assume a job that crashed mid-send didn't partially succeed.

# Judge invitations (US4, T040/T041)

Keycloak Admin API creates the user with a temp password + `UPDATE_PASSWORD` required action,
idempotent if the user already exists. MailKit sends app-side (not Keycloak's own email) so
per-recipient status/retry is tracked in `Invitation`.

# Results dispatch (US10, T074-T076)

Finalize → enqueue pipeline `GeneratePdfs → BundleZip → SendResultEmail`, one PDF per beer entry
via QuestPDF (`ScoreSheetDocument`: blind code, style, sections, comments, total, consolidated
mean, judge display name per R-14 — check `research.md` R-14 before deciding what judge identity
detail, if any, belongs on a results PDF). ZIP path scheme is fixed:
`/Competition/Participant/Style_BlindCode.pdf`.

Per-recipient granularity: a failure emailing one participant must not block or roll back PDFs/
emails already sent to others. Status and retry are per-recipient, surfaced to the organizer
(T077) with a manual retry action (T076 endpoint).

Both pipelines emit progress over `CompetitionHub` (`DispatchProgress` per `realtime-hub-events`)
to the organizer group only — never to judges.
