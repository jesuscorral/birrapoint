import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

import { ApiClient } from './api-client.service';

export type ImportRowStatus =
  'Valid' | 'StyleMismatch' | 'CategoryMismatch' | 'CategoryStyleMismatch' | 'Invalid' | 'Excluded';

// Wire shape `data` on ImportRowDto (contracts/rest-api.md §Entry Import,
// backend/.../Features/Import/ImportDtos.cs ImportRowDataDto) — category/style are the raw
// parsed cell text (may not resolve to anything); competitionCategoryId/resolvedStyleCode are
// the resolved references, set at parse time or by the organizer via the full row edit below.
export interface ImportRowData {
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

export interface ImportRow {
  rowNumber: number;
  status: ImportRowStatus;
  data: ImportRowData;
  error: string | null;
}

export interface ImportBatch {
  importId: string;
  rows: ImportRow[];
}

// Wire body for PUT .../rows/{rowNumber} (EditImportRowRequest) — mirrors ImportRowDataDto minus
// the read-only raw category/style cell text, and uses `styleCode` (not `resolvedStyleCode`) for
// the resolved style since this is the request the organizer is asserting, not the parsed result.
export interface EditImportRowRequest {
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

export interface ConsolidatedEntry {
  id: string;
  blindCode: string;
  styleCode: string;
}

export interface ConsolidateResult {
  imported: number;
  excluded: number;
  entries: ConsolidatedEntry[];
}

@Injectable({ providedIn: 'root' })
export class ImportApiService {
  private readonly apiClient = inject(ApiClient);

  upload(competitionId: string, file: File): Observable<ImportBatch> {
    const body = new FormData();
    body.append('file', file);
    return this.apiClient.post<ImportBatch>(`/competitions/${competitionId}/imports`, body);
  }

  getImport(competitionId: string, importId: string): Observable<ImportBatch> {
    return this.apiClient.get<ImportBatch>(`/competitions/${competitionId}/imports/${importId}`);
  }

  editRow(
    competitionId: string,
    importId: string,
    rowNumber: number,
    body: EditImportRowRequest,
  ): Observable<ImportRow> {
    return this.apiClient.put<ImportRow>(
      `/competitions/${competitionId}/imports/${importId}/rows/${rowNumber}`,
      body,
    );
  }

  excludeRow(competitionId: string, importId: string, rowNumber: number): Observable<ImportRow> {
    return this.apiClient.post<ImportRow>(
      `/competitions/${competitionId}/imports/${importId}/rows/${rowNumber}/exclude`,
      {},
    );
  }

  consolidate(competitionId: string, importId: string): Observable<ConsolidateResult> {
    return this.apiClient.post<ConsolidateResult>(
      `/competitions/${competitionId}/imports/${importId}/consolidate`,
      {},
    );
  }

  revalidate(competitionId: string, importId: string): Observable<ImportBatch> {
    return this.apiClient.post<ImportBatch>(
      `/competitions/${competitionId}/imports/${importId}/revalidate`,
      {},
    );
  }
}
