import 'fake-indexeddb/auto';

import { db } from './db';
import type { DraftRow, OutboxRow } from './db';

describe('BirraPointDb', () => {
  afterEach(async () => {
    await db.drafts.clear();
    await db.outbox.clear();
  });

  it('declares the drafts and outbox stores with the contracted key paths', () => {
    expect(db.drafts.schema.primKey.keyPath).toBe('beerEntryId');
    expect(db.outbox.schema.primKey.keyPath).toBe('idempotencyKey');
  });

  it('round-trips a draft row keyed by beerEntryId', async () => {
    const draft: DraftRow = {
      beerEntryId: 'entry-1',
      tastingTableId: 'table-1',
      scores: { aroma: 10, appearance: 2, flavor: 15, mouthfeel: 4, overall: 8 },
      comments: {
        aroma: 'Citrus and pine hop aroma, moderate intensity.',
        appearance: 'Deep golden, persistent white head, brilliant.',
        flavor: 'Balanced malt backbone with resinous hop finish.',
        mouthfeel: 'Medium body, lively carbonation, dry finish.',
        overall: 'A clean, well-executed example of the style.',
      },
      updatedAt: new Date().toISOString(),
    };

    await db.drafts.put(draft);
    const stored = await db.drafts.get('entry-1');

    expect(stored).toEqual(draft);
  });

  it('round-trips an outbox row keyed by idempotencyKey and supports attempt bookkeeping', async () => {
    const row: OutboxRow = {
      idempotencyKey: 'comp-1:table-1:judge-1:entry-1',
      tastingTableId: 'table-1',
      beerEntryId: 'entry-1',
      scores: { aroma: 10, appearance: 2, flavor: 15, mouthfeel: 4, overall: 8 },
      comments: {
        aroma: 'Citrus and pine hop aroma, moderate intensity.',
        appearance: 'Deep golden, persistent white head, brilliant.',
        flavor: 'Balanced malt backbone with resinous hop finish.',
        mouthfeel: 'Medium body, lively carbonation, dry finish.',
        overall: 'A clean, well-executed example of the style.',
      },
      attempts: 0,
    };

    await db.outbox.put(row);
    await db.outbox.update(row.idempotencyKey, {
      attempts: 1,
      lastAttemptAt: new Date().toISOString(),
    });
    const stored = await db.outbox.get(row.idempotencyKey);

    expect(stored?.attempts).toBe(1);
    expect(stored?.lastAttemptAt).toBeDefined();
  });
});
