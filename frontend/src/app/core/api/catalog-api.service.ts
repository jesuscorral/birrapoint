import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { of, tap } from 'rxjs';

import { ApiClient } from './api-client.service';

// GET /styles/{code} response shape (contracts/rest-api.md §Catalog, FR-049).
export interface StyleVitalStatistics {
  ogLow: number | null;
  ogHigh: number | null;
  fgLow: number | null;
  fgHigh: number | null;
  ibuLow: number | null;
  ibuHigh: number | null;
  srmLow: number | null;
  srmHigh: number | null;
  abvLow: number | null;
  abvHigh: number | null;
}

export interface StyleDescription {
  overallImpression: string;
  aroma: string;
  appearance: string;
  flavor: string;
  mouthfeel: string;
  comments: string;
  history: string;
  characteristicIngredients: string;
  styleComparison: string;
  entryInstructions: string | null;
  commercialExamples: string[];
  tags: string[];
}

export interface StyleDetail {
  code: string;
  name: string;
  categoryNumber: string;
  categoryName: string;
  vitalStatistics: StyleVitalStatistics;
  description: StyleDescription;
}

// GET /styles response shape (contracts/rest-api.md §Catalog) — the lightweight catalog list,
// also used by the entry-import wizard step's style picker/correction UI.
export interface StyleSummary {
  code: string;
  name: string;
  categoryNumber: string;
  categoryName: string;
}

/**
 * BJCP 2021 catalog reference data (FR-049) — cross-cutting, not owned by any one feature:
 * `GET /styles/{code}` (full detail) lands here because it's consumed judge-side too (the
 * evaluation-sheet style reference panel). `GET /styles` (lightweight list) is used by both the
 * competition-wizard's step-3 category picker and its step-4 entry-import style picker.
 */
@Injectable({ providedIn: 'root' })
export class CatalogApiService {
  private readonly apiClient = inject(ApiClient);

  // In-memory only, for the lifetime of this (root-provided, session-long) service instance — not
  // a persistent Dexie-backed cache. The BJCP catalog is static reference data, not
  // competition-specific state, so this is enough to satisfy "cached for offline use" (T060B):
  // re-toggling the reference panel doesn't refetch, and a mid-session connectivity drop doesn't
  // blank an already-loaded panel. A page reload still requires connectivity for the first fetch
  // of any given style — a bigger, Dexie-backed cache was judged out of scope for this task.
  private readonly cache = new Map<string, StyleDetail>();

  getStyleDetail(code: string): Observable<StyleDetail> {
    const cached = this.cache.get(code);
    if (cached) {
      return of(cached);
    }
    return this.apiClient
      .get<StyleDetail>(`/styles/${code}`)
      .pipe(tap((detail) => this.cache.set(code, detail)));
  }

  getStyles(): Observable<StyleSummary[]> {
    return this.apiClient.get<StyleSummary[]>('/styles');
  }
}
