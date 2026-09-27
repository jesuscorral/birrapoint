# 0007 - SignalR event payloads serialize enums as strings, not the JSON default int

**Status:** Accepted
**Date:** 2026-07-14

## Context

SignalR's JSON hub protocol has its own `JsonSerializerOptions`; by default enum fields such as
`DispatchProgress.status` go out as integers. The database (ADR-0004) and the contracts use
strings.

## Decision

`AddSignalR().AddJsonProtocol(...)` adds a `JsonStringEnumConverter` for every hub payload. REST
responses use the same string representation through the HTTP JSON options.

## Consequences

- Every event's enum fields are self-describing (`"Completed"`), consistent across database, REST
  and SignalR.
- The setting is global: no event can opt back into ints without changing shared configuration.
- New enum members are additive on the wire.
