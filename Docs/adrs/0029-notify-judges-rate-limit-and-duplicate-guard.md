# 0029 - Notify-judges rate limit and duplicate-invitation guard

**Status:** Accepted
**Date:** 2026-10-25

## Context
`POST /competitions/{id}/judges/notify` (FR-059) sends one invitation email, with a fresh temporary password, to every `Pending` judge. Double clicks, retries and scripts could trigger it repeatedly: duplicate emails, rotated passwords and Keycloak/SMTP load (FR-065, T147). Two separate problems:

1. Abuse and accidental bursts on a costly endpoint.
2. Duplicate invitations for the same judge when two requests overlap, which a rate limit cannot fully prevent.

## Decision
**Rate limit** (`Common/RateLimiting/RateLimitingExtensions.cs`), ASP.NET Core built-in limiter, policy `NotifyJudges` on that endpoint only:
- Fixed window, partitioned per JWT `sub`: one organizer cannot throttle another. No `sub` means no limiter (authorization rejects it anyway).
- Default 2 requests per 30 s (`RateLimiting:NotifyJudges:PermitLimit` / `WindowSeconds`, must be positive). One legitimate click plus one retry fits; a spam loop does not. A legitimate organizer notifies a few times per competition.
- `UseRateLimiter` runs after `UseAuthorization`, so anonymous calls get `401` and do not consume budget.
- Rejection: `429`, RFC 7807 `urn:birrapoint:rate-limited` (error catalog entry 15) and an integer `Retry-After` in seconds.
- `AppHost` sets `PermitLimit=1000` locally because the Playwright suite shares one organizer across parallel specs. Cloud keeps the default.

**Duplicate guard** (`Features/Judges/NotifyJudges.cs`), at the data level: the handler locks the competition row (`SELECT ... FOR UPDATE`) in a transaction and skips judges that already have a `Pending`/`Running` `SendInvitation` `DispatchJob`. The worker is signalled after commit. Concurrent notifies serialize on the lock, so each judge gets at most one in-flight job.

Alternatives considered:

| Option | Why not |
|---|---|
| Global fixed window | One organizer could lock out all others |
| Sliding window / token bucket | Smoother, but more state and no benefit for a 2-per-30 s abuse cap |
| Distributed store (Redis, DB counter) | New infrastructure for one endpoint; the API runs as a single replica |
| Rate limit only, no lock | Limiter allows 2 overlapping requests; they would double-send |

## Consequences
- Positive: no new dependency; duplicate prevention is correct regardless of limiter settings or replica count; clear, documented `429` contract.
- Limiter state is in memory per replica. Azure (`apps.tf`) and AWS (`ecs.tf`) pin the API to exactly one replica, so the limit is exact. With N replicas the effective limit becomes N x `PermitLimit`; the duplicate guard still holds, so only the abuse cap loosens. Revisit with a shared store if the API ever scales out.
- A restart resets the window.
- The notify handler now holds a row lock for the duration of its transaction; it only enqueues jobs, so the hold is short.
- Frontend must handle `rate-limited` (use `Retry-After`).
