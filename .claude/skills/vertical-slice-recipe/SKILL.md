---
name: "vertical-slice-recipe"
description: "The exact file layout and wiring for one backend vertical slice (request + handler + validator + Minimal API endpoint) in BirraPoint's MediatR/EF Core stack. Load before implementing any Features/<Capability>/ backend task."
user-invocable: true
disable-model-invocation: false
---

# One capability = one folder

`Features/<Capability>/`. A slice inside it typically needs:

- `<Verb><Noun>.cs` — the MediatR `IRequest<TResponse>` record, its `IRequestHandler<,>`, and its
  `AbstractValidator<>` (FluentValidation), colocated in one file unless it grows unwieldy — this
  project favors one file per slice over one-class-per-file layering.
- Endpoint mapping — an extension method (`Map<Capability>Endpoints`) called from `Program.cs`,
  using Minimal API (`MapGet`/`MapPost`/etc.), never an MVC controller.
- `JudgeDtos/` subfolder for any judge-facing projection (see `blind-tasting-integrity`).

# Pipeline

Validation runs in the MediatR pipeline (`Common/Behaviors/ValidationBehavior.cs`) before the
handler executes — the handler should never manually re-check what the validator already covers.

# Errors

Throw/return the existing `DomainException` types mapped to `urn:birrapoint:*`
(`Common/Errors/DomainErrorCatalog.cs`) — `400` for validation, `409` for domain/state conflicts,
`404` for out-of-scope resources (never reveal existence to the wrong caller). Don't invent a new
HTTP status or urn without checking `contracts/rest-api.md` first.

# Persistence

`AppDbContext` injected via constructor/primary constructor; EF Core parameterized queries only;
respect existing unique indexes and check constraints from `data-model.md` rather than
re-validating them redundantly in the handler.

# Slice isolation

Cross-slice interaction only via MediatR messages or shared contracts — a handler in
`Features/Evaluations` must never reach into `Features/Tables`' internals directly.

# After the slice

Emit any relevant event via `Realtime/EventPublisher.cs` (see `realtime-hub-events`) and write an
`AuditLog` entry via `IAuditWriter` (`Common/Audit/`) if the action is one that FR-035/FR-039
requires auditing (organizer corrections, state changes, removals).

# Testing order (Principle III, non-negotiable)

Unit test for the validator/handler logic first, then the integration/contract test against
`WebApplicationFactory` + Testcontainers, both failing, before any implementation code is written.
