import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

import { ApiClient } from './api-client.service';

export type BeerImportRowStatus =
  'Valid' | 'StyleMismatch' | 'CategoryMismatch' | 'CategoryStyleMismatch' | 'Invalid' | 'Excluded';

// Wire shape `data` on BeerImportRowDto (contracts/rest-api.md §Entry Import,
// src/BirraPoint.Api/.../Features/Import/CommonComponents/Dtos/BeerImportRowDataDto.cs) — category/style are the raw
// parsed cell text (may not resolve to anything); competitionCategoryId/resolvedStyleCode are
// the resolved references, set at parse time or by the organizer via the full row edit below.
export interface BeerImportRowData {
  participantName: string | null;
  participantEmail: string | null;
  acceMemberNumber: string | null;
  dateOfBirth: string | null;
  phone: string | null;
  category: string | null;
  competitionCategoryId: string | null;
  style: string | null;
  resolvedStyleCode: string | null;
  submittedAt: string | null;
  abvPercent: number | null;
  brewDate: string | null;
  bottlingDate: string | null;
  malts: string | null;
  hops: string | null;
  yeast: string | null;
  otherIngredients: string | null;
  entryInstructions: string | null;
  beerName: string | null;
}

export interface BeerImportRow {
  rowNumber: number;
  status: BeerImportRowStatus;
  data: BeerImportRowData;
  error: string | null;
}

export interface BeerImportBatch {
  importId: string;
  rows: BeerImportRow[];
}

// Wire body for PUT .../rows/{rowNumber} (EditBeerImportRowRequest) — mirrors BeerImportRowDataDto minus
// the read-only raw category/style cell text, and uses `styleCode` (not `resolvedStyleCode`) for
// the resolved style since this is the request the organizer is asserting, not the parsed result.
export interface EditBeerImportRowRequest {
  participantName: string;
  participantEmail: string | null;
  acceMemberNumber: string | null;
  dateOfBirth: string | null;
  phone: string | null;
  competitionCategoryId: string | null;
  styleCode: string | null;
  submittedAt: string;
  abvPercent: number;
  brewDate: string | null;
  bottlingDate: string | null;
  malts: string | null;
  hops: string | null;
  yeast: string | null;
  otherIngredients: string | null;
  entryInstructions: string | null;
  beerName: string | null;
}

export interface BeerImportConsolidatedEntry {
  id: string;
  blindCode: string;
  styleCode: string;
}

export interface BeerImportConsolidateResult {
  imported: number;
  excluded: number;
  entries: BeerImportConsolidatedEntry[];
}

@Injectable({ providedIn: 'root' })
export class BeerImportApiService {
  private readonly apiClient = inject(ApiClient);

  upload(competitionId: string, file: File): Observable<BeerImportBatch> {
    const body = new FormData();
    body.append('file', file);
    return this.apiClient.post<BeerImportBatch>(
      `/competitions/${competitionId}/imports/beers`,
      body,
    );
  }

  getImport(competitionId: string, importId: string): Observable<BeerImportBatch> {
    return this.apiClient.get<BeerImportBatch>(
      `/competitions/${competitionId}/imports/beers/${importId}`,
    );
  }

  editRow(
    competitionId: string,
    importId: string,
    rowNumber: number,
    body: EditBeerImportRowRequest,
  ): Observable<BeerImportRow> {
    return this.apiClient.put<BeerImportRow>(
      `/competitions/${competitionId}/imports/beers/${importId}/rows/${rowNumber}`,
      body,
    );
  }

  excludeRow(
    competitionId: string,
    importId: string,
    rowNumber: number,
  ): Observable<BeerImportRow> {
    return this.apiClient.post<BeerImportRow>(
      `/competitions/${competitionId}/imports/beers/${importId}/rows/${rowNumber}/exclude`,
      {},
    );
  }

  consolidate(competitionId: string, importId: string): Observable<BeerImportConsolidateResult> {
    return this.apiClient.post<BeerImportConsolidateResult>(
      `/competitions/${competitionId}/imports/beers/${importId}/consolidate`,
      {},
    );
  }

  revalidate(competitionId: string, importId: string): Observable<BeerImportBatch> {
    return this.apiClient.post<BeerImportBatch>(
      `/competitions/${competitionId}/imports/beers/${importId}/revalidate`,
      {},
    );
  }
}
