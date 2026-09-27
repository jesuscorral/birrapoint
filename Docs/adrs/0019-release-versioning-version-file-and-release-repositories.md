# 0019 - Release versioning: `version.txt` on main, separate `-release` repositories

**Status:** Accepted
**Date:** 2026-09-26

## Context

FR-064(b) needs a manual release pipeline that builds a chosen branch or commit, publishes
immutable semver-tagged images, takes the version from a file in the repo and advances it. User
decisions (2026-09-26): released images kept apart from CI's `latest` (Docker Hub has no folders),
releases possible from hotfix branches without unreleased `main` changes, and the workflow pushes
the version bump to `main` itself (`main` is unprotected).

## Decision

1. **Separate repositories**: `<ns>/birrapoint-<c>-release:X.Y.Z` for releases;
   `<ns>/birrapoint-<c>` keeps only `latest` and `sha-<short>`.
2. **`version.txt` at the repo root** holds the next version and is always read from `main`, so
   versions form one sequence and hotfixes take the next number.
3. **Build and publish first**: dispatchable only from `main`; `ref` must be `main`, `hotfix/*` or
   an existing `v*` tag, resolved once to a SHA. It runs the reusable quality gates on that SHA,
   pushes the three images, then creates tag `vX.Y.Z` and a GitHub Release (image digests +
   generated notes).
4. **Then bump `version.txt` on `main`** (`bump`: patch default, minor, major) with `GITHUB_TOKEN`,
   whose pushes trigger no workflows. If `main` becomes protected, a GitHub App on the bypass list
   replaces it.
5. **Never overwrite a release**: refuse to start if the tag or any `X.Y.Z` image exists, re-check
   before each push; an image already pushed by the same release (matching
   `org.opencontainers.image.revision`) is reused, so "Re-run failed jobs" completes a partial
   release. Version logic in `.github/scripts/version.sh`, tested by `version.test.sh`.

## Consequences

- Hotfix flow: branch from `vX.Y.Z`, fix, release with `ref=hotfix/...`, then bring the fix to
  `main` in its own PR.
- A hotfix consumes `main`'s next number; choose `bump` accordingly for the following release.
- One queued run at a time (`release` concurrency group).
- Required status checks must use the names `gates / backend` and `gates / frontend`.
- Release builds read but never write the `main` layer cache.
- Unused releases leave harmless version gaps; releases are cut on demand, not per change.
