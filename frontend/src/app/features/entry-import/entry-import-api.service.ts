import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

import { ApiClient } from '../../core/api/api-client.service';

export type ImportRowStatus = 'Valid' | 'StyleMismatch' | 'Invalid' | 'Excluded';

// Wire shape `data` on ImportRowDto (contracts/rest-api.md §Entry Import) — the raw parsed cells.
export interface ImportRowData {
  participantName: string | null;
  participantEmail: string | null;
  beerName: string | null;
  style: string | null;
  collaborators: string[];
  resolvedStyleCode: string | null;
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

export interface StyleSummary {
  code: string;
  name: string;
  categoryNumber: string;
  categoryName: string;
}

export type ResolveRowAction = 'assign-style' | 'exclude';

@Injectable({ providedIn: 'root' })
export class EntryImportApiService {
  private readonly apiClient = inject(ApiClient);

  upload(competitionId: string, file: File): Observable<ImportBatch> {
    const body = new FormData();
    body.append('file', file);
    return this.apiClient.post<ImportBatch>(`/competitions/${competitionId}/imports`, body);
  }

  getImport(competitionId: string, importId: string): Observable<ImportBatch> {
    return this.apiClient.get<ImportBatch>(`/competitions/${competitionId}/imports/${importId}`);
  }

  resolveRow(
    competitionId: string,
    importId: string,
    rowNumber: number,
    action: ResolveRowAction,
    styleCode?: string,
  ): Observable<ImportRow> {
    return this.apiClient.put<ImportRow>(
      `/competitions/${competitionId}/imports/${importId}/rows/${rowNumber}`,
      { action, styleCode },
    );
  }

  consolidate(competitionId: string, importId: string): Observable<ConsolidateResult> {
    return this.apiClient.post<ConsolidateResult>(
      `/competitions/${competitionId}/imports/${importId}/consolidate`,
      {},
    );
  }

  getStyles(): Observable<StyleSummary[]> {
    return this.apiClient.get<StyleSummary[]>('/styles');
  }
}
