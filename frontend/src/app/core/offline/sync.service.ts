import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { ApiClient } from '../api/api-client.service';
import { ApiError } from '../api/api-error';
import { db } from './db';
import type { DraftRow, EvaluationComments, EvaluationScores, OutboxRow } from './db';

// SC-003/FR-026: a draft must be durable within 300ms of the judge's last change, but rapid
// keystrokes must coalesce into a single Dexie write rather than one per keystroke — a debounce
// (not a throttle) at exactly the contracted bound satisfies both at once.
const DRAFT_DEBOUNCE_MS = 300;

// Capped exponential backoff for outbox retries (R-08), loosely mirroring the backend's
// DispatchRetryPolicy shape (attempt count -> next-attempt delay) translated to the client: 1s,
// 2s, 4s, ... capped at 60s so a long offline stretch doesn't spin the network on reconnect.
const BACKOFF_BASE_MS = 1000;
const BACKOFF_CAP_MS = 60_000;

interface DraftWaiter {
  resolve: () => void;
  reject: (error: unknown) => void;
}

/** Wire success shape of POST /me/tables/{tableId}/evaluations (contracts/rest-api.md §Judge workspace). */
export interface SubmitEvaluationResult {
  evaluationId: string;
  status: string;
  total: number;
  discrepancy: unknown;
}

function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    return error.detail ?? error.title;
  }
  return error instanceof Error ? error.message : 'Unknown error';
}

/**
 * The offline engine (R-08): Dexie `drafts` (in-progress editing, debounced ≤300ms) and `outbox`
 * (submitted-but-unsynced) are a cache, never the source of truth — every write here is
 * reconciled against the server via replay, not assumed to have "won" once persisted locally.
 *
 * Deliberately does NOT use the Background Sync API (unsupported on iOS Safari, R-08) — replay is
 * driven by three explicit triggers instead: the `window` `online` event (registered below), this
 * service's own construction (a `providedIn: 'root'` singleton is created once per session, on
 * first injection — in this app that's effectively "app start" for the offline-capable judge
 * workspace, without wiring a separate APP_INITIALIZER/root-component dependency), and immediately
 * after each `submit()` call.
 */
@Injectable({ providedIn: 'root' })
export class SyncService {
  private readonly apiClient = inject(ApiClient);

  private readonly draftTimers = new Map<string, ReturnType<typeof setTimeout>>();
  private readonly draftWaiters = new Map<string, DraftWaiter[]>();
  private replaying = false;

  constructor() {
    window.addEventListener('online', () => void this.replayOutbox());
    // "App start" trigger — see class doc. Fire-and-forget: nothing in the constructor path can
    // usefully await this, and a slow/failed first attempt is not fatal (the online listener and
    // every subsequent submit() still cover it).
    void this.replayOutbox();
  }

  /**
   * Debounced write to the `drafts` store (FR-026/SC-003). Returns a Promise that settles once the
   * write this call ultimately contributes to (its own, or a later call within the same debounce
   * window that superseded it) completes — callers that want to detect a storage failure (quota
   * exceeded, private-browsing restrictions) can `.catch()` it; callers that just want fire-and-
   * forget behavior can ignore the returned Promise entirely.
   */
  saveDraft(
    beerEntryId: string,
    tastingTableId: string,
    scores: EvaluationScores,
    comments: EvaluationComments,
  ): Promise<void> {
    const existingTimer = this.draftTimers.get(beerEntryId);
    if (existingTimer) {
      clearTimeout(existingTimer);
    }

    return new Promise<void>((resolve, reject) => {
      const waiters = this.draftWaiters.get(beerEntryId) ?? [];
      waiters.push({ resolve, reject });
      this.draftWaiters.set(beerEntryId, waiters);

      const timer = setTimeout(() => {
        this.draftTimers.delete(beerEntryId);
        const pending = this.draftWaiters.get(beerEntryId) ?? [];
        this.draftWaiters.delete(beerEntryId);

        const row: DraftRow = {
          beerEntryId,
          tastingTableId,
          scores,
          comments,
          updatedAt: new Date().toISOString(),
        };

        db.drafts
          .put(row)
          .then(() => pending.forEach((waiter) => waiter.resolve()))
          .catch((error: unknown) => pending.forEach((waiter) => waiter.reject(error)));
      }, DRAFT_DEBOUNCE_MS);
      this.draftTimers.set(beerEntryId, timer);
    });
  }

  loadDraft(beerEntryId: string): Promise<DraftRow | undefined> {
    return db.drafts.get(beerEntryId);
  }

  clearDraft(beerEntryId: string): Promise<void> {
    return db.drafts.delete(beerEntryId);
  }

  /**
   * Durable-first submit (R-08): the outbox row is written — and must successfully persist —
   * before anything else happens, so the judge's work survives a reload even if the network
   * attempt below never completes. A rejection here (storage unavailable: quota exceeded,
   * private-browsing restrictions, etc.) propagates to the caller, per the spec edge case that the
   * judge must be warned immediately rather than silently losing offline protection.
   *
   * While offline, this resolves immediately after the durable write with `{ status: 'enqueued' }`
   * — no network attempt, no possibility of hanging.
   *
   * While online, it *does* await one immediate attempt (unlike replayOutbox()'s background
   * sweep) so the caller can react to a definitive rejection — a 400/409 the server actively
   * refused (e.g. `out-of-sequence`, `order-not-fixed`). The row still stays in the outbox either
   * way (durability always wins over cleanliness — background replay might yet succeed later, e.g.
   * once a race with another device clears), but the caller is told about it *now*, as a thrown
   * error, instead of being falsely told "all good" while it silently retries in the background.
   * Any other failure (no connectivity despite `navigator.onLine` being stale/optimistic, a 5xx, a
   * timeout) is treated as transient and resolves `{ status: 'enqueued' }` just like the offline
   * case — the offline-first guarantee is that a flaky connection never blocks or errors the
   * judge's "submit" action, only a definitive server rejection does.
   */
  async submit(
    idempotencyKey: string,
    tastingTableId: string,
    beerEntryId: string,
    scores: EvaluationScores,
    comments: EvaluationComments,
  ): Promise<{ status: 'confirmed' | 'enqueued' }> {
    const row: OutboxRow = {
      idempotencyKey,
      tastingTableId,
      beerEntryId,
      scores,
      comments,
      attempts: 0,
    };
    await db.outbox.put(row);

    if (!navigator.onLine) {
      return { status: 'enqueued' };
    }

    try {
      await this.sendOne(row);
      // Fire-and-forget sweep of any *other* still-pending rows now that we know we're online —
      // this one is separate from and doesn't block the return.
      void this.replayOutbox();
      return { status: 'confirmed' };
    } catch (error) {
      await this.recordFailedAttempt(row, error);
      if (this.isDefinitiveRejection(error)) {
        throw error;
      }
      return { status: 'enqueued' };
    }
  }

  /**
   * Replays every due outbox row against the server. A row is "due" if it has never been
   * attempted, or if enough time has passed since its last attempt per the backoff schedule.
   * `navigator.onLine` false short-circuits the whole pass — no point burning a retry attempt (and
   * skewing its backoff clock) on a call that's certain to fail for lack of connectivity, not a
   * server-side reason.
   */
  async replayOutbox(): Promise<void> {
    if (this.replaying) {
      return;
    }
    this.replaying = true;
    try {
      if (!navigator.onLine) {
        return;
      }

      const rows = await db.outbox.toArray();
      const now = Date.now();
      for (const row of rows) {
        if (this.isDue(row, now)) {
          await this.attemptOne(row);
        }
      }
    } finally {
      this.replaying = false;
    }
  }

  private isDue(row: OutboxRow, now: number): boolean {
    if (!row.lastAttemptAt) {
      return true;
    }
    const elapsed = now - new Date(row.lastAttemptAt).getTime();
    return elapsed >= this.backoffDelayMs(row.attempts);
  }

  private backoffDelayMs(attempts: number): number {
    const delay = BACKOFF_BASE_MS * 2 ** Math.max(0, attempts - 1);
    return Math.min(delay, BACKOFF_CAP_MS);
  }

  // Used by replayOutbox()'s background sweep, where a single row's failure must never interrupt
  // the loop over the rest of the outbox — errors are recorded (attempts/backoff), not rethrown.
  private async attemptOne(row: OutboxRow): Promise<void> {
    try {
      await this.sendOne(row);
    } catch (error) {
      await this.recordFailedAttempt(row, error);
    }
  }

  // The actual POST (contracts/rest-api.md §Judge workspace) plus its on-success cleanup. Shared
  // by submit()'s foreground attempt and attemptOne()'s background one — only their failure
  // handling differs (submit() may rethrow a definitive rejection; attemptOne() never does).
  private async sendOne(row: OutboxRow): Promise<SubmitEvaluationResult> {
    // A 200 (idempotent replay, R-07) and a 201 (fresh insert) both resolve HttpClient's
    // Observable normally — nothing here needs to branch on status code to dedupe correctly;
    // either way, the server has it and this outbox row is done.
    const result = await firstValueFrom(
      this.apiClient.post<SubmitEvaluationResult>(
        `/me/tables/${row.tastingTableId}/evaluations`,
        { beerEntryId: row.beerEntryId, scores: row.scores, comments: row.comments },
        { headers: { 'X-Idempotency-Key': row.idempotencyKey } },
      ),
    );
    await db.outbox.delete(row.idempotencyKey);
    await db.drafts.delete(row.beerEntryId);
    return result;
  }

  private async recordFailedAttempt(row: OutboxRow, error: unknown): Promise<void> {
    await db.outbox.update(row.idempotencyKey, {
      attempts: row.attempts + 1,
      lastAttemptAt: new Date().toISOString(),
      lastError: describeError(error),
    });
  }

  // A request the server actively refused because the payload/request is invalid for its current
  // state (validation, or a domain conflict like out-of-sequence/order-not-fixed) — submit()'s
  // foreground attempt surfaces this to the caller immediately rather than silently reporting
  // "enqueued", even though the row still stays queued for a later background retry regardless
  // (see submit()'s doc comment). Everything else (no response at all, 5xx, timeout) is transient.
  private isDefinitiveRejection(error: unknown): boolean {
    return error instanceof ApiError && (error.status === 400 || error.status === 409);
  }
}
