# 0007 - SignalR event payloads serialize enums as strings, not the JSON default int

**Status:** Accepted
**Date:** 2026-07-14

## Context

T016 introduces the first typed SignalR event payload, `DispatchProgressPayload { JobType,
Status, Detail }` (`Common/Jobs/DispatchProgressPayload.cs`), carrying `DispatchJobType` and
`DispatchJobStatus` enum fields. `Microsoft.AspNetCore.SignalR`'s built-in JSON hub protocol uses
`System.Text.Json` with its own, separately configured `JsonSerializerOptions` — untouched, an
enum field serializes as its underlying `int` (e.g. `2`, not `"Completed"`).

That would be the only place in the system where an enum crosses the wire as a number: the domain
already stores state/status enums as strings in PostgreSQL for readability (ADR-0004), and the
worked examples in `contracts/rest-api.md` and `contracts/signalr-hub.md` (e.g. `{ competitionId,
state }` for `CompetitionStateChanged`) read naturally as strings like `"Draft"` or
`"PendingConsensus"`, not as opaque integers a client would have to look up against the C# enum
definition to interpret.

## Decision

Configure `AddSignalR().AddJsonProtocol(...)` in `Program.cs` to add a `JsonStringEnumConverter`
to the hub protocol's `PayloadSerializerOptions`. This applies to **every** event payload sent
over `CompetitionHub`, not just `DispatchProgress` — every future story's event record
(`CompetitionStateChanged`, `TableOrderFixed`, etc.) inherits the same string representation for
any enum field it carries, with no per-event opt-in required.

REST responses are a separate serializer path (`Microsoft.AspNetCore.Http.Json` /
`ConfigureHttpJsonOptions`) and are not affected by this change — that configuration doesn't exist
yet (no REST endpoint has shipped). T017, the first REST slice, should apply the equivalent
`JsonStringEnumConverter` there so both transports agree on the same wire format; a reviewer
should flag a REST DTO that leaks an int-valued enum against this ADR.

## Consequences

- Every SignalR event payload's enum fields are self-describing on the wire (`"Completed"`, not
  `2`), matching the DB-level convention (ADR-0004) and the contract's worked examples — no
  lookup table needed by any client (organizer dashboard, judge app) to interpret a status.
- This is a global hub-protocol setting, not per-payload: a future story cannot silently opt an
  event back into int serialization without changing this shared configuration, which is the
  intended guardrail.
- Adding a new enum member is still additive and non-breaking on the wire (a new string value); a
  client written against the old enum set simply doesn't recognize the new string yet, same
  forward-compatibility profile as the existing REST contract.
