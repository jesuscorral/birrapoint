# 0019 - Release versioning: `version.txt` on main, separate `-release` repositories

**Status:** Accepted
**Date:** 2026-09-26

## Context

FR-064(b) asks for a manually triggered release pipeline. It must build a selected branch or
commit and publish immutable, semver-tagged images. It must take the version from a file in the
repository and advance that file for the next release.

Three constraints shaped the design. They are user decisions from 2026-09-26:

- **Separate release storage.** Released images must be kept apart from the `latest` images that
  `ci.yml` publishes on every merge. The request was for "a `/release` folder" on Docker Hub,
  which has no folders.
- **Hotfixes.** A release may come from a hotfix branch, not only from `main`. The release must
  not carry unreleased `main` changes.
- **Pushing the bump.** The workflow commits the version bump to `main` itself. `main` is not
  protected today.

## Decision

1. **Separate repositories on Docker Hub.** Released images go to separate repositories,
   `<ns>/birrapoint-<component>-release:X.Y.Z`. The integration repositories
   `<ns>/birrapoint-<component>` keep only `latest` and `sha-<short>`. Permissions, retention and
   browsing stay independent, and a deploy can only select a real release by construction.
2. **`version.txt` at the repository root holds the version the next release will publish.**
   The release pipeline always reads it from `main`, never from the ref it builds, so `main` is
   the single sequence of versions. A hotfix therefore gets the next number, and no number is
   ever reused.
3. **The pipeline builds the `ref` input and publishes the images before anything else.** It
   runs only when dispatched from `main`, so the definition that publishes releases and pushes
   to `main` is always `main`'s own. The `ref` input must be `main`, a `hotfix/*` branch or an
   existing `v*` tag; it is resolved once to a SHA. The pipeline runs the same quality
   gates as CI (`quality-gates.yml`, reusable) on that SHA, then pushes the three images, and
   only then creates the annotated tag `vX.Y.Z` on that SHA and a GitHub Release. The release
   notes list the image digests, followed by the notes GitHub generates.
4. **Afterwards it advances `version.txt` on `main`.** The `bump` input chooses patch (the
   default), minor or major. The bot uses `GITHUB_TOKEN` (`contents: write`), whose pushes
   trigger no workflows, and `ci.yml` also path-ignores `version.txt`. If `main` becomes
   protected, a GitHub App on the ruleset's bypass list replaces `GITHUB_TOKEN` for this step.
5. **A released version is never overwritten.** The pipeline refuses to start when `vX.Y.Z` or
   any `X.Y.Z` image already exists, and each image job re-checks right before pushing. The one
   exception is an image the same release already pushed before its job failed: an image whose
   `org.opencontainers.image.revision` label equals the released SHA is reused, digest included.
   Together with idempotent finalize steps, this makes "Re-run failed jobs" complete a partially
   failed release. If the Docker Hub plan offers immutable tags, enabling them on the `-release`
   repositories adds a registry-side backstop.
   The version logic is in `.github/scripts/version.sh`, covered by
   `.github/scripts/version.test.sh`; the `workflows.yml` workflow runs those tests and
   actionlint.

## Consequences

- **Hotfix flow.** Branch from `vX.Y.Z`, fix, and run the release with `ref=hotfix/...`. The fix
  must then reach `main` through its own PR (cherry-pick); the pipeline does not merge back.
- **Hotfixes take `main`'s next number.** After a hotfix, `main`'s following release may ship
  features under a patch bump; choose `bump` (or edit `version.txt`) accordingly.
- **One queued run.** The `release` concurrency group keeps a single pending run: a third dispatch
  while one runs and one waits replaces the waiting one.
- **Check names.** The reusable gates report as `gates / backend` and `gates / frontend`; any
  required-status-check configuration must use those names.
- **Minor and major versions.** Bump through the `bump` input of the preceding release, or edit
  `version.txt` in a PR.
- **Release builds do not share the `latest` cache.** They read the GitHub Actions layer cache
  that `main` writes but never write to it, so a hotfix build may be slower.
- **Unused releases.** A release whose deployment is never run still consumes a version number;
  versions are cheap and gaps are harmless.
- **No release for docs-only changes.** Nothing forces a release per change. Releases are cut
  when someone decides to ship.
