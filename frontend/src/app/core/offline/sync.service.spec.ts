import 'fake-indexeddb/auto';

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';
import { db } from './db';
import type { EvaluationComments, EvaluationScores, OutboxRow } from './db';
import { SyncService } from './sync.service';

const API_URL = `${environment.apiBaseUrl}/api/v1`;

function scoresFixture(overrides: Partial<EvaluationScores> = {}): EvaluationScores {
  return { aroma: 10, appearance: 2, flavor: 15, mouthfeel: 4, overall: 8, ...overrides };
}

function commentsFixture(overrides: Partial<EvaluationComments> = {}): EvaluationComments {
  return {
    aroma: 'Citrus and pine hop aroma, moderate intensity.',
    appearance: 'Deep golden, persistent white head, brilliant.',
    flavor: 'Balanced malt backbone with resinous hop finish.',
    mouthfeel: 'Medium body, lively carbonation, dry finish.',
    overall: 'A clean, well-executed example of the style.',
    ...overrides,
  };
}

// fake-indexeddb dispatches its request events via a macrotask (setImmediate/setTimeout(0), not a
// microtask — see fake-indexeddb/build/cjs/lib/scheduling.js), so a fire-and-forget async chain
// that reads/writes Dexie (submit()'s background replayOutbox(), the constructor's own initial
// "app start" replay) needs real macrotask turns — not just Promise microtask ticks — before its
// outstanding HTTP request registers with httpMock.
async function flush(): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await new Promise((resolve) => setTimeout(resolve, 0));
  }
}

describe('SyncService', () => {
  let httpMock: HttpTestingController;

  // Debounce tests run under jest.useFakeTimers(), where flush()'s real setTimeout ticks would
  // never fire without an explicit advance — they don't need the settle-first behavior below
  // anyway (the outbox is always empty at construction time in this suite, so the "app start"
  // replay it fires is a same-tick no-op that never touches db.drafts, the only store those tests
  // spy on).
  function injectService(): SyncService {
    return TestBed.inject(SyncService);
  }

  async function createService(): Promise<SyncService> {
    const service = injectService();
    // Let the constructor's own "app start" replay attempt (fired against whatever is/isn't in
    // the outbox at construction time) settle before the test seeds its own state — otherwise its
    // still-in-flight reentrancy guard (`replaying`) would swallow an explicit replayOutbox() call
    // the test makes immediately after.
    await flush();
    return service;
  }

  beforeEach(async () => {
    await db.drafts.clear();
    await db.outbox.clear();
    Object.defineProperty(navigator, 'onLine', { value: true, configurable: true });

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(async () => {
    httpMock.verify();
    await db.drafts.clear();
    await db.outbox.clear();
    jest.restoreAllMocks();
    jest.useRealTimers();
  });

  describe('saveDraft debounce contract', () => {
    beforeEach(() => {
      jest.useFakeTimers();
    });

    it('does not write to Dexie before 300ms have passed since the last change', async () => {
      const putSpy = jest.spyOn(db.drafts, 'put').mockResolvedValue('entry-1');
      const service = injectService();

      void service.saveDraft('entry-1', 'table-1', scoresFixture(), commentsFixture());
      await jest.advanceTimersByTimeAsync(299);

      expect(putSpy).not.toHaveBeenCalled();
    });

    it('commits within 300ms of the last change', async () => {
      const putSpy = jest.spyOn(db.drafts, 'put').mockResolvedValue('entry-1');
      const service = injectService();

      void service.saveDraft('entry-1', 'table-1', scoresFixture(), commentsFixture());
      await jest.advanceTimersByTimeAsync(300);

      expect(putSpy).toHaveBeenCalledTimes(1);
    });

    it('coalesces rapid successive changes into a single write carrying the latest values', async () => {
      const putSpy = jest.spyOn(db.drafts, 'put').mockResolvedValue('entry-1');
      const service = injectService();

      service.saveDraft('entry-1', 'table-1', scoresFixture({ aroma: 1 }), commentsFixture());
      await jest.advanceTimersByTimeAsync(100);
      service.saveDraft('entry-1', 'table-1', scoresFixture({ aroma: 5 }), commentsFixture());
      await jest.advanceTimersByTimeAsync(100);
      service.saveDraft('entry-1', 'table-1', scoresFixture({ aroma: 9 }), commentsFixture());
      await jest.advanceTimersByTimeAsync(300);

      expect(putSpy).toHaveBeenCalledTimes(1);
      expect(putSpy).toHaveBeenCalledWith(
        expect.objectContaining({ scores: expect.objectContaining({ aroma: 9 }) }),
      );
    });

    it('debounces writes independently per beerEntryId', async () => {
      const putSpy = jest.spyOn(db.drafts, 'put').mockResolvedValue('entry-1');
      const service = injectService();

      service.saveDraft('entry-1', 'table-1', scoresFixture(), commentsFixture());
      await jest.advanceTimersByTimeAsync(150);
      service.saveDraft('entry-2', 'table-1', scoresFixture(), commentsFixture());
      await jest.advanceTimersByTimeAsync(300);

      expect(putSpy).toHaveBeenCalledTimes(2);
    });
  });

  describe('draft save/load/clear round trip', () => {
    it('round-trips a draft through the real Dexie store', async () => {
      const service = await createService();
      const scores = scoresFixture();
      const comments = commentsFixture();

      await service.saveDraft('entry-1', 'table-1', scores, comments);
      const loaded = await service.loadDraft('entry-1');

      expect(loaded?.scores).toEqual(scores);
      expect(loaded?.comments).toEqual(comments);
      expect(loaded?.tastingTableId).toBe('table-1');

      await service.clearDraft('entry-1');
      expect(await service.loadDraft('entry-1')).toBeUndefined();
    }, 10_000);

    it('loadDraft resolves undefined when no draft exists', async () => {
      const service = await createService();
      expect(await service.loadDraft('no-such-entry')).toBeUndefined();
    });
  });

  describe('submit()', () => {
    it('writes to the outbox durably before the network attempt resolves, then confirms on success', async () => {
      const service = await createService();

      const outcome = service.submit(
        'table-1:entry-1',
        'table-1',
        'entry-1',
        scoresFixture(),
        commentsFixture(),
      );

      // Durable-first: readable well before the network round-trip below ever completes.
      await flush();
      const stored = await db.outbox.get('table-1:entry-1');
      expect(stored).toBeDefined();
      expect(stored?.tastingTableId).toBe('table-1');
      expect(stored?.beerEntryId).toBe('entry-1');

      httpMock.expectOne(`${API_URL}/me/tables/table-1/evaluations`).flush({
        evaluationId: 'ev-1',
        status: 'Confirmed',
        total: 39,
        discrepancy: null,
      });

      expect(await outcome).toEqual({ status: 'confirmed' });
      expect(await db.outbox.get('table-1:entry-1')).toBeUndefined();
    });

    it('does not attempt a network call while offline, resolves "enqueued", and leaves the row queued', async () => {
      Object.defineProperty(navigator, 'onLine', { value: false, configurable: true });
      const service = await createService();

      const outcome = await service.submit(
        'table-1:entry-1',
        'table-1',
        'entry-1',
        scoresFixture(),
        commentsFixture(),
      );
      await flush();

      expect(outcome).toEqual({ status: 'enqueued' });
      httpMock.expectNone(`${API_URL}/me/tables/table-1/evaluations`);
      expect(await db.outbox.get('table-1:entry-1')).toBeDefined();
    });

    it('rejects when the durable outbox write itself fails (storage unavailable)', async () => {
      const service = await createService();
      jest.spyOn(db.outbox, 'put').mockRejectedValue(new Error('QuotaExceededError'));

      await expect(
        service.submit('table-1:entry-1', 'table-1', 'entry-1', scoresFixture(), commentsFixture()),
      ).rejects.toThrow('QuotaExceededError');
    });

    it('propagates a definitive 409 from an immediate online attempt, but keeps the row queued for a later retry', async () => {
      const service = await createService();

      const outcome = service.submit(
        'table-1:entry-1',
        'table-1',
        'entry-1',
        scoresFixture(),
        commentsFixture(),
      );
      await flush();
      httpMock
        .expectOne(`${API_URL}/me/tables/table-1/evaluations`)
        .flush(
          { type: 'urn:birrapoint:out-of-sequence', title: 'Out of sequence' },
          { status: 409, statusText: 'Conflict' },
        );

      await expect(outcome).rejects.toMatchObject({ status: 409 });

      const stored = await db.outbox.get('table-1:entry-1');
      expect(stored).toBeDefined();
      expect(stored?.attempts).toBe(1);
      expect(stored?.lastError).toBeDefined();
    });

    it('does not reject on a transient/network-level failure while online — resolves "enqueued" for background retry instead', async () => {
      const service = await createService();

      const outcome = service.submit(
        'table-1:entry-1',
        'table-1',
        'entry-1',
        scoresFixture(),
        commentsFixture(),
      );
      await flush();
      httpMock
        .expectOne(`${API_URL}/me/tables/table-1/evaluations`)
        .error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

      await expect(outcome).resolves.toEqual({ status: 'enqueued' });
      const stored = await db.outbox.get('table-1:entry-1');
      expect(stored).toBeDefined();
      expect(stored?.attempts).toBe(1);
    });

    it('times out a hung request instead of waiting forever, treating it as transient (resolves "enqueued")', async () => {
      // HttpClient has no default timeout — a dead socket while navigator.onLine is (stale-)true
      // would otherwise hang submit() indefinitely (senior-code-reviewer finding on PR #22).
      // Real timers up through createService()/the initial "app start" replay settling (same as
      // every other test here), then fake timers just for this test so a 15s wait resolves
      // instantly instead of actually elapsing in the test run.
      const service = await createService();
      jest.useFakeTimers();

      const outcome = service.submit(
        'table-1:entry-1',
        'table-1',
        'entry-1',
        scoresFixture(),
        commentsFixture(),
      );
      // Let the durable outbox write settle — fake-indexeddb dispatches its request events via a
      // macrotask, potentially several nested hops deep (see the `flush()` helper's own comment
      // above, which loops 5 real-timer ticks for the same reason), so under fake timers this
      // needs a few `advanceTimersByTimeAsync` passes (each flushes microtasks too), not a single
      // one, before the HTTP call reliably registers with httpMock.
      for (let i = 0; i < 5; i++) {
        await jest.advanceTimersByTimeAsync(0);
      }

      // Registers the pending mock request as matched (so httpMock.verify() in afterEach doesn't
      // complain about an unmatched expectation) — never flushed, since the whole point is that it
      // hangs; RxJS's timeout() unsubscribes from it once the deadline passes, which Angular's
      // testing backend itself marks as "cancelled," a terminal state verify() accepts.
      httpMock.expectOne(`${API_URL}/me/tables/table-1/evaluations`);
      await jest.advanceTimersByTimeAsync(15_001); // SUBMIT_TIMEOUT_MS + 1, kept local — not exported

      await expect(outcome).resolves.toEqual({ status: 'enqueued' });

      const stored = await db.outbox.get('table-1:entry-1');
      expect(stored).toBeDefined();
      expect(stored?.attempts).toBe(1);
    });
  });

  describe('replayOutbox()', () => {
    async function seedOutboxRow(overrides: Partial<OutboxRow> = {}): Promise<void> {
      await db.outbox.put({
        idempotencyKey: 'table-1:entry-1',
        tastingTableId: 'table-1',
        beerEntryId: 'entry-1',
        scores: scoresFixture(),
        comments: commentsFixture(),
        attempts: 0,
        ...overrides,
      });
    }

    it('sends a queued outbox row and removes it (and the matching draft) on success', async () => {
      const service = await createService();
      await seedOutboxRow();
      await db.drafts.put({
        beerEntryId: 'entry-1',
        tastingTableId: 'table-1',
        scores: scoresFixture(),
        comments: commentsFixture(),
        updatedAt: new Date().toISOString(),
      });

      const replay = service.replayOutbox();
      await flush();
      httpMock.expectOne(`${API_URL}/me/tables/table-1/evaluations`).flush({
        evaluationId: 'ev-1',
        status: 'Confirmed',
        total: 39,
        discrepancy: null,
      });
      await replay;

      expect(await db.outbox.get('table-1:entry-1')).toBeUndefined();
      expect(await db.drafts.get('entry-1')).toBeUndefined();
    });

    it('dedupes on a 200 idempotent-replay response exactly like a 201 (both clear the outbox row)', async () => {
      const service = await createService();
      await seedOutboxRow();

      const replay = service.replayOutbox();
      await flush();
      const req = httpMock.expectOne(`${API_URL}/me/tables/table-1/evaluations`);
      req.flush(
        { evaluationId: 'ev-1', status: 'Confirmed', total: 39, discrepancy: null },
        { status: 200, statusText: 'OK' },
      );
      await replay;

      expect(await db.outbox.get('table-1:entry-1')).toBeUndefined();
    });

    it('records attempts/lastError and backs off, rather than removing the row, on failure', async () => {
      const service = await createService();
      await seedOutboxRow();

      const replay = service.replayOutbox();
      await flush();
      httpMock
        .expectOne(`${API_URL}/me/tables/table-1/evaluations`)
        .flush(
          { type: 'urn:birrapoint:out-of-sequence', title: 'Out of sequence' },
          { status: 409, statusText: 'Conflict' },
        );
      await replay;

      const stored = await db.outbox.get('table-1:entry-1');
      expect(stored?.attempts).toBe(1);
      expect(stored?.lastAttemptAt).toBeDefined();
      expect(stored?.lastError).toBeDefined();
    });

    it('skips a row whose backoff window has not elapsed yet', async () => {
      const service = await createService();
      await seedOutboxRow({
        attempts: 1,
        lastAttemptAt: new Date(Date.now() - 400).toISOString(), // well under the 1s base delay
      });

      await service.replayOutbox();

      httpMock.expectNone(`${API_URL}/me/tables/table-1/evaluations`);
    });

    it('retries a row once its backoff window has elapsed', async () => {
      const service = await createService();
      await seedOutboxRow({
        attempts: 1,
        lastAttemptAt: new Date(Date.now() - 1500).toISOString(), // past the 1s base delay
      });

      const replay = service.replayOutbox();
      await flush();
      httpMock.expectOne(`${API_URL}/me/tables/table-1/evaluations`).flush({
        evaluationId: 'ev-1',
        status: 'Confirmed',
        total: 39,
        discrepancy: null,
      });
      await replay;

      expect(await db.outbox.get('table-1:entry-1')).toBeUndefined();
    });

    it('is triggered by the window "online" event', async () => {
      const service = await createService();
      void service; // the listener is registered by construction; nothing else to call here
      await seedOutboxRow();

      window.dispatchEvent(new Event('online'));
      await flush();

      httpMock.expectOne(`${API_URL}/me/tables/table-1/evaluations`).flush({
        evaluationId: 'ev-1',
        status: 'Confirmed',
        total: 39,
        discrepancy: null,
      });
      await flush();

      expect(await db.outbox.get('table-1:entry-1')).toBeUndefined();
    });

    it('is triggered once on construction ("app start"), before any explicit call', async () => {
      await seedOutboxRow();

      // Deliberately bypass the createService() helper's settle-then-seed ordering: seed first,
      // *then* construct, so the constructor's own initial replay is what picks this row up.
      TestBed.inject(SyncService);
      await flush();

      httpMock.expectOne(`${API_URL}/me/tables/table-1/evaluations`).flush({
        evaluationId: 'ev-1',
        status: 'Confirmed',
        total: 39,
        discrepancy: null,
      });
      await flush();

      expect(await db.outbox.get('table-1:entry-1')).toBeUndefined();
    });
  });
});
