# 0042 - Integration events with a transactional outbox

**Status:** Accepted
**Date:** 2026-11-09

## Context
Modules reacted to each other through in-process MediatR notifications that wrote into the other module's tables inside the publisher's transaction (finalize -> PDF jobs, table judges -> BOS flags). That coupled module persistence, kept no record of what was delivered, and cannot survive splitting modules into services. Three flows were also missing (judge email change, correction after `Finalized`, conflict alert). Constraints: PostgreSQL only, single API replica today, MediatR pinned 12.x, no new paid dependencies, Dapr pub/sub planned in #108.

## Decision
- New block `src/BuildingBlocks/BuildingBlocks.Messaging` (agnostic of BirraPoint, ADR-0035/0037): `IIntegrationEventPublisher`, `IIntegrationEventHandler<T>`, outbox dispatcher, inbox runner, retention purger.
- **Outbox per publishing module**, same shape in each module schema (`organization`, `judging`, `publication`). The publisher stages the row in the caller's `SaveChanges`, so the state change and the event commit atomically. A dispatcher wakes after commit (plus a safety-net poll), claims rows with a lease (`SKIP LOCKED`), retries with backoff and marks `Dead` after `MaxAttempts`.
- **Transport behind `IIntegrationEventTransport`**. `LocalTransport` invokes in-process subscribers; #108 adds a Dapr implementation without touching publishers, handlers or contracts.
- **At-least-once delivery, inbox de-duplication** on `(EventId, Handler)`, committed in the same transaction as the handler's effect.
- **Order-insensitive handlers**: payloads carry identifiers only; handlers recompute from current state and do nothing when the target is gone.
- Events live in `Modules/<M>/Contracts/IntegrationEvents/`; other modules reference only that namespace. In-module requests stay on MediatR.
- Retention purge of `Delivered` (7 d) and `Dead` (30 d) outbox rows and inbox rows (14 d), in batches. `Pending`/`Delivering` are never purged. Config in the `Messaging` section.
- Realtime pushes from subscribers go out only after the delivery commits.

## Consequences
- Easier: modules own their writes; delivery is observable and retried; moving to a broker is a transport change; the missing flows exist.
- Harder: effects are eventually consistent (sub-second locally); handlers must be idempotent and order-insensitive; each event needs a stable type name and version.
- Rejected: MassTransit / Wolverine / CAP (a framework and its conventions, and for CAP its own schema, for one monolith; overlaps the existing job queue); a broker now (new infrastructure on both clouds before any service split); keeping MediatR notifications (no atomic cross-module guarantee, nothing persisted).
- Open: a `GeneratePdfs` run still in progress when a correction lands can write an old-score PDF after `RefreshResults` (accepted, listed in the living architecture debt). `Dead` rows have no UI.
