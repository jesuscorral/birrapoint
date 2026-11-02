# 0005 - Seed the BJCP catalog via an embedded JSON resource and migration-time InsertData

**Status:** Accepted
**Date:** 2026-07-09

> Update 2026-11-02 (ADR-0034): the catalog now belongs to the Catalog module. The resource is `Modules/Catalog/Features/Catalog/Data/bjcp-2021.json` and the loader is `Modules/Catalog/Persistence/Seeding/BjcpStyleCatalogLoader`. The paths below are the original ones.

## Context

The full BJCP 2021 catalog (125 styles, vital statistics and guide text, FR-049) must exist in
every environment, seeded by an EF Core migration (R-12). `HasData` would copy ~200 KB of text into
generated migration and snapshot code, duplicating `Features/Catalog/Data/bjcp-2021.json`.

## Decision

- `bjcp-2021.json` is an `EmbeddedResource`, so it is available regardless of working directory
  (dev, Testcontainers, containers).
- `Common/Persistence/Seeding/BjcpStyleCatalogLoader` reads it (shared kernel, never a feature
  slice). The `AddBjcpStyleCatalogDetails` migration calls it and seeds through parameterized
  `InsertData`.
- `BjcpStyle.Code` / `BeerEntry.StyleCode` are `varchar(20)` to fit slug codes such as
  `27-KentuckyCommon`.

## Consequences

- The JSON is the single source of truth and the migration stays small.
- Editing the JSON would not update already-migrated databases. A unit test pins the file's
  SHA-256 (`ComputeContentHash()`), so any edit fails the build until it ships with a follow-up
  migration that updates the rows and the pinned hash.
- The loader DTOs must stay compatible with the historical migration; changing their shape needs
  a new migration.
