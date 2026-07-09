# 0005 - Seed the BJCP catalog via an embedded JSON resource and migration-time InsertData

**Status:** Accepted
**Date:** 2026-07-09

## Context

T010 requires the full BJCP 2021 style catalog (125 entries, categories 1–34 + Appendix B, each
with vital statistics and a complete guide description, FR-049) to be present as read-only
reference data in every environment. R-12 specifies it must be "seeded through an EF Core
migration", not seeded at application startup, so every environment (dev, CI/Testcontainers,
production) ends up with an identical, testable catalog as soon as migrations run.

The catalog data itself already lives in `Features/Catalog/Data/bjcp-2021.json` (T010, ~200 KB of
Spanish guide text). Two ways to get it into a migration were considered:

1. **`HasData`/model-snapshot seeding** — the idiomatic EF pattern, but it bakes every field of
   every one of the 125 rows into the generated migration and model-snapshot source as literal
   C# `InsertData` calls, duplicating the JSON content inside compiled code. Any future catalog
   correction would require regenerating a migration and touching two files in lockstep.
2. **Read the JSON at migration-apply time** — the migration's `Up()` calls a small loader that
   reads the packaged JSON and builds the `InsertData` call programmatically. The migration file
   stays small; the JSON file remains the single source of truth.

## Decision

- Mark `bjcp-2021.json` as an `EmbeddedResource` in `BirraPoint.Api.csproj` so it ships inside the
  compiled assembly and is available identically regardless of working directory (`dotnet run`,
  Testcontainers-hosted integration tests, or a published container image).
- `Features/Catalog/Data/BjcpStyleCatalogLoader.cs` reads the embedded resource and deserializes
  it into `BjcpStyleSeedRecord` DTOs; this loader is shared by the seed migration and by a
  DB-free unit test (`BjcpStyleSeedDataTests`) that validates the JSON's shape independently of
  any database.
- The `AddBjcpStyleCatalogDetails` migration's `Up()` calls the loader and passes the parsed rows
  to `migrationBuilder.InsertData(...)` (parameterized — no string-escaping risk despite the
  free-text Spanish content, e.g. apostrophes). `Down()` deletes all seeded rows before narrowing
  `Code`/`BeerEntry.StyleCode` back to `varchar(5)`, since several slug-style codes (e.g.
  `27-KentuckyCommon`) no longer fit that width.

## Consequences

- The catalog has exactly one source of truth (the JSON file); correcting a style's text only
  ever touches that file plus a fresh migration, never a hand-edited `InsertData` literal.
- The migration file stays a few hundred lines instead of the several-thousand-line file `HasData`
  would have generated for 125 rows with long free-text fields.
- The loader is a small piece of production code that must stay backward-compatible with
  historical migrations: if `BjcpStyleSeedRecord`'s shape changes later, the already-applied
  `AddBjcpStyleCatalogDetails` migration would break on a fresh database unless the DTO change is
  itself paired with a new migration (same caveat as any schema change to seeded reference data).
- `Code`/`BeerEntry.StyleCode` were widened from `string(5)` to `string(20)` (data-model.md
  amendment) to accommodate synthetic slug codes for styles with no official BJCP letter subcode
  (Historical Beer, Appendix B, named Specialty-IPA variants).
