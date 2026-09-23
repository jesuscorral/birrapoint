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
 * Session 2026-09-21 (FR-063): structured tasting-sheet descriptors, additive alongside
 * EvaluationScores/EvaluationComments above — mirrors backend's EvaluationDescriptorsDto
 * (Features/Evaluations/EvaluationDescriptors.cs) field-for-field, camelCase both ways. Every
 * field is optional, both at the top level and within each nested section: a judge fills as many
 * or as few as they like. None of it feeds the score/total. Enum-like fields (color/clarity/foam,
 * offFlavors) use the same English tokens as the backend's closed lists
 * (evaluation-descriptor-catalog.ts) — the Spanish labels shown in the UI are a display-only
 * mapping, never sent on the wire.
 */
export interface EvaluationDescriptors {
  appearance?: AppearanceDescriptors;
  aroma?: AromaDescriptors;
  flavor?: FlavorDescriptors;
  mouthfeel?: MouthfeelDescriptors;
  overall?: OverallDescriptors;
  offFlavors?: string[];
}

export interface AppearanceDescriptors {
  color?: string | null;
  colorOther?: string | null;
  colorInappropriate?: boolean;
  clarity?: string | null;
  clarityInappropriate?: boolean;
  foam?: string | null;
  foamOther?: string | null;
  foamInappropriate?: boolean;
  /** Continuous 0–100 bipolar slider (Baja↔Alta). */
  retention?: number | null;
  retentionInappropriate?: boolean;
  texture?: string | null;
  notes?: string | null;
}

/** Discrete 4-stop intensity sliders (Nada/Bajo/Medio/Alto = 0–3). */
export interface AromaDescriptors {
  malt?: number | null;
  maltInappropriate?: boolean;
  hops?: number | null;
  hopsInappropriate?: boolean;
  fermentation?: number | null;
  fermentationInappropriate?: boolean;
}

export interface FlavorDescriptors {
  malt?: number | null;
  maltInappropriate?: boolean;
  hops?: number | null;
  hopsInappropriate?: boolean;
  bitterness?: number | null;
  bitternessInappropriate?: boolean;
  fermentation?: number | null;
  fermentationInappropriate?: boolean;
  /** Continuous 0–100 bipolar slider, Lupulado↔Maltoso. */
  balance?: number | null;
  balanceInappropriate?: boolean;
  /** Continuous 0–100 bipolar slider, Seco↔Dulce. */
  finish?: number | null;
  finishInappropriate?: boolean;
}

export interface MouthfeelDescriptors {
  body?: number | null;
  bodyInappropriate?: boolean;
  carbonation?: number | null;
  carbonationInappropriate?: boolean;
  alcoholWarmth?: number | null;
  alcoholWarmthInappropriate?: boolean;
  creaminess?: number | null;
  creaminessInappropriate?: boolean;
  astringency?: number | null;
  astringencyInappropriate?: boolean;
  notes?: string | null;
}

/** Three continuous 0–100 bipolar sliders. */
export interface OverallDescriptors {
  classicExample?: number | null;
  classicExampleInappropriate?: boolean;
  defects?: number | null;
  defectsInappropriate?: boolean;
  vitality?: number | null;
  vitalityInappropriate?: boolean;
}

/**
 * In-progress evaluation-sheet fields (data-model.md §Client-side stores), written ≤300 ms after
 * each change (SC-003) and deleted on successful submit. `tastingTableId` isn't part of the key
 * but is needed to know where the draft eventually submits — and, like `OutboxRow`'s own
 * `tastingTableId` index, is indexed (v2) because T087 needs to look up "drafts for this table"
 * directly when a judge is removed, not only the ones that happen to have a matching outbox row.
 */
export interface DraftRow {
  beerEntryId: string;
  tastingTableId: string;
  scores: EvaluationScores;
  comments: EvaluationComments;
  /** Session 2026-09-21 (FR-063) — optional, not indexed, so no Dexie version bump was needed. */
  descriptors?: EvaluationDescriptors;
  feedback?: string;
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
  /** Session 2026-09-21 (FR-063) — optional, not indexed, so no Dexie version bump was needed. */
  descriptors?: EvaluationDescriptors;
  feedback?: string;
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
    // v2 (T087): index drafts by tastingTableId too, so rejectOutboxForTable() can purge every
    // draft for a table directly — not only the ones that happen to already have a matching
    // outbox row.
    this.version(2).stores({
      drafts: 'beerEntryId, tastingTableId',
    });
  }
}

export const db = new BirraPointDb();
