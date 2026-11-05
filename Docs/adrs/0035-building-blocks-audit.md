# 0035 - BuildingBlocks: reusable cross-cutting blocks, Audit first

**Status:** Accepted
**Date:** 2026-11-04

## Context
Some backend capabilities (audit trail, and later others) are not BirraPoint business logic and would be useful in other services. They lived in `BirraPoint.Api/Common/` and depended on BirraPoint types: the audit writer used `AppDbContext`, `ICurrentUser` (Keycloak subject) and an `AuditLog` entity deriving from BirraPoint's `Entity` (whose `CreatedAt`/`UpdatedAt` duplicated `OccurredAt` on an insert-only table). The goal is to extract them into blocks that BirraPoint references today and that can later move to a shared repository and be consumed by other repositories.

## Decision
Cross-cutting capabilities move to `backend/src/BuildingBlocks/BuildingBlocks.<Capability>/`, one project per capability, tested in `backend/tests/BuildingBlocks/BuildingBlocks.<Capability>.Tests/`. Rules for a block:

- No reference to any BirraPoint project or type; a test asserts it. The host plugs in what the block cannot know through small interfaces (e.g. who the actor is).
- Public API in the `BuildingBlocks.<Capability>` namespace only, documented, with defaults that suit any host and hooks to refine them.
- Depends only on framework or well-known packages it really needs; versions come from `Directory.Packages.props` while it lives here; `net10.0` set literally, not through `$(BirraPointTargetFramework)`.
- Tests use their own host-agnostic fixtures (Testcontainers PostgreSQL, no EF InMemory), not BirraPoint's.

**BuildingBlocks.Audit** (first block) depends on `Microsoft.EntityFrameworkCore.Relational` only (no provider):

| Type | Role |
|---|---|
| `AuditLog` | Entity: `Id` (Guid v7), `ActorUserId`, `Action`, `EntityType`, `EntityId`, `DataJson` (`{ before, after }`), `OccurredAt` |
| `IAuditWriter` / `AuditWriter<TContext>` | `Record(action, entityType, entityId, before, after)` stages the row on the host DbContext without `SaveChanges`, so it commits atomically with the business change; time from `TimeProvider` |
| `IAuditActorAccessor` | Implemented by the host: actor identifier |
| `ModelBuilder.ApplyAuditLog(tableName = "AuditLogs", configure)` | Maps the entity with neutral defaults; the host sets provider specifics (BirraPoint: `jsonb`) |
| `services.AddAudit<TContext, TActorAccessor>()` | Scoped writer and actor; `TimeProvider.System` unless the host registered one |

BirraPoint keeps the same `IAuditWriter.Record` calls (only the namespace changes), implements `CurrentUserAuditActor` (Keycloak `sub`) and maps the entity in `AppDbContext`. Migration `DropAuditLogTimestamps` removes `AuditLogs.CreatedAt`/`UpdatedAt`; `Down` recreates them from `OccurredAt`.

**BuildingBlocks.EmailSender** (second block) depends on `MailKit` and `Microsoft.Extensions.Options.ConfigurationExtensions`:

| Type | Role |
|---|---|
| `IEmailSender` | `SendAsync` / `SendWithAttachmentsAsync(toEmail, subject, htmlBody, ...)`; one HTML email to one recipient |
| `EmailAttachment` | `FileName`, `Content` (`ReadOnlyMemory<byte>`), `ContentType` |
| `SmtpOptions` | `Host`, `Port`, `UseStartTls`, `From`, `Username`/`Password`; no defaults of any host; validated on every send (missing host/from, port outside 1-65535, invalid mailbox, user without password), so a host without SMTP still starts |
| `services.AddEmailSender(section, configure)` | Binds the section after the host's `configure` defaults (configuration wins); singleton MailKit sender (internal) unless the host registered an `IEmailSender`; options read through `IOptionsMonitor`, so reloads apply without a restart |

BirraPoint binds the `Smtp` section and supplies `BirraPoint <no-reply@birrapoint.local>` as the default `From` (previously hard-coded in `SmtpSettings`); the AWS/Azure deployments keep setting `Smtp__From`. Tests run against a real Mailpit container, with and without SMTP authentication.

## Consequences
- Audit can be reused by another service without BirraPoint code, and tested on its own.
- The audit entity is no longer part of BirraPoint's shared kernel and is not `ITimestamped`; `OccurredAt` is the only timestamp of an audit row.
- The API image must copy every referenced block (`src/BuildingBlocks/`) into the build context.
- Moving the blocks to their own repository later means publishing them as NuGet packages and replacing the `ProjectReference`s; the public API is what other repositories will depend on, so changes to it become breaking changes.
- Rejected: one `BuildingBlocks` project for every capability (every consumer would carry every dependency); a dependency-free abstractions package plus an EF Core adapter (more moving parts without a second storage today); keeping the redundant timestamp columns via shadow properties (keeps BirraPoint concerns in the block's table).
