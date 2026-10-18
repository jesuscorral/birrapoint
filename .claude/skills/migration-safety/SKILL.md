---
name: "migration-safety"
description: "Review a new or changed EF Core migration (backend/src/BirraPoint.Api/Common/Persistence/Migrations) before it ships: data loss, locking, constraints on existing rows, invariant-backing indexes, model snapshot and data-model.md sync. Use whenever a diff adds or edits a migration."
user-invocable: true
disable-model-invocation: false
---

# Migration Safety Review

Production runs Postgres 16 on Neon and migrations apply on API start, so a bad migration either
loses competition data or blocks startup during a live event. Review the migration's `Up`/`Down`,
its `.Designer.cs` and `AppDbContextModelSnapshot.cs` changes against the entity changes in
the same diff.

Find the migrations in the change: `git diff main...HEAD --name-only -- '*Migrations*'` (plus
`git status` for uncommitted ones).

## Checks

1. **Data loss.** Every `DropColumn`, `DropTable`, `AlterColumn` that narrows a type/length, or
   rename done as drop + add (EF does this when it can't infer a rename) loses data. Flag unless the
   task explicitly removes that data; a rename must use `RenameColumn`/`RenameTable`.
2. **Existing rows.** `AddColumn` with `nullable: false` needs a `defaultValue` (or a backfill via
   `migrationBuilder.Sql` before the constraint). New unique indexes or FKs over existing tables
   fail if current data violates them — say what data could.
3. **Invariant-backing constraints stay intact.** Unique `(JudgeId, BeerEntryId)` on evaluations
   (idempotent sync, CLAUDE.md invariant 4) and any unique index that makes a one-shot flip or a
   job claim safe must not be dropped or weakened. Check with the entity configuration, not memory.
4. **Locking and size.** Index creation, type changes and `Sql` backfills on large tables
   (evaluations, audit log, dispatch jobs) lock writes; note the risk and keep data-heavy SQL
   batched.
5. **Anonymity.** No entrant-identifying column (name, participant, brewery, origin) added to a
   judge-facing table or view (BR-01).
6. **Raw SQL.** `migrationBuilder.Sql` must be static and parameter-free; no data that depends on
   environment, no secrets.
7. **Model sync.** Snapshot matches the entity configuration (run
   `dotnet ef migrations has-pending-model-changes --project backend/src/BirraPoint.Api` when
   available); `Down` reverses `Up`; `specs/001-birrapoint-mvp/data-model.md` reflects the schema
   change in the same diff.
8. **One migration per change.** Several migrations for one task that was never released should be
   squashed (remove and re-add) before merge.

## Report format

Table: `file:line | check | issue | severity (blocker/warning) | fix`.

Blockers = unintended data loss, a startup-breaking constraint on existing data, a weakened
invariant constraint, or an anonymity leak. Report only; fix after confirmation.
