import Dexie from 'dexie';
import type { Table } from 'dexie';

export interface EvaluationScores {
  aroma: number;
  appearance: number;
  flavor: number;
  mouthfeel: number;
  overall: number;
}

export interface EvaluationComments {
  aroma: string;
  appearance: string;
  flavor: string;
  mouthfeel: string;
  overall: string;
}

/**
 * In-progress evaluation-sheet fields (data-model.md §Client-side stores), written ≤300 ms after
 * each change (SC-003) and deleted on successful submit. `tastingTableId` isn't part of the key
 * but is needed to know where the draft eventually submits.
 */
export interface DraftRow {
  beerEntryId: string;
  tastingTableId: string;
  scores: EvaluationScores;
  comments: EvaluationComments;
  updatedAt: string;
}

/**
 * Submitted-but-unsynced evaluation payload + attempt metadata (R-08), keyed by the deterministic
 * `{competitionId}:{tableId}:{judgeId}:{entryId}` idempotency key (R-07). `tastingTableId` is
 * indexed (not just embedded in the key) because T087 needs to look up "outbox items for this
 * table" when a judge is removed.
 */
export interface OutboxRow {
  idempotencyKey: string;
  tastingTableId: string;
  beerEntryId: string;
  scores: EvaluationScores;
  comments: EvaluationComments;
  attempts: number;
  lastAttemptAt?: string;
  lastError?: string;
}

/** IndexedDB via Dexie (R-08) — a cache, never the source of truth; a device wipe loses only unsynced work (FR-027). */
export class BirraPointDb extends Dexie {
  drafts!: Table<DraftRow, string>;
  outbox!: Table<OutboxRow, string>;

  constructor() {
    super('birrapoint');
    this.version(1).stores({
      drafts: 'beerEntryId',
      outbox: 'idempotencyKey, tastingTableId',
    });
  }
}

export const db = new BirraPointDb();
