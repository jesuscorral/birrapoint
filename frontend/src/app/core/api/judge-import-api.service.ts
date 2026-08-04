import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

import { ApiClient } from './api-client.service';

export type JudgeImportRowStatus = 'Valid' | 'Invalid' | 'Excluded';

// Wire shape `data` on JudgeImportRowDto (contracts/rest-api.md §Judge Roster Import,
// contracts/judge-import-file.md) — unlike the beer-entry import, no field here resolves against
// a catalog; every field is stored verbatim as free text.
export interface JudgeImportRowData {
  name: string | null;
  email: string | null;
  bjcpRank: string | null;
  bjcpId: string | null;
  preferredCategory: string | null;
  preferences: string | null;
}

export interface JudgeImportRow {
  rowNumber: number;
  status: JudgeImportRowStatus;
  data: JudgeImportRowData;
  error: string | null;
}

export interface JudgeImportBatch {
  importId: string;
  rows: JudgeImportRow[];
}

// Wire body for PUT .../rows/{rowNumber} — full replace of every field in JudgeImportRowData.
export interface EditJudgeImportRowRequest {
  name: string | null;
  email: string | null;
  bjcpRank: string | null;
  bjcpId: string | null;
  preferredCategory: string | null;
  preferences: string | null;
}

export interface ConsolidatedJudge {
  id: string;
  email: string;
}

// Wire shape `skipped` on the consolidate response (contracts/rest-api.md §Judge Roster Import,
// FR-058) — same JudgeSkipDto backend reuses from the plain email-list flow, but this endpoint
// only ever populates the "duplicate-in-list" reason (an already-registered judge is upserted,
// not skipped, here).
export interface JudgeImportSkip {
  email: string;
  reason: 'duplicate-in-list';
}

export interface JudgeImportConsolidateResult {
  created: ConsolidatedJudge[];
  updated: ConsolidatedJudge[];
  excluded: number;
  skipped: JudgeImportSkip[];
}

@Injectable({ providedIn: 'root' })
export class JudgeImportApiService {
  private readonly apiClient = inject(ApiClient);

  upload(competitionId: string, file: File): Observable<JudgeImportBatch> {
    const body = new FormData();
    body.append('file', file);
    return this.apiClient.post<JudgeImportBatch>(
      `/competitions/${competitionId}/judge-imports`,
      body,
    );
  }

  getImport(competitionId: string, importId: string): Observable<JudgeImportBatch> {
    return this.apiClient.get<JudgeImportBatch>(
      `/competitions/${competitionId}/judge-imports/${importId}`,
    );
  }

  editRow(
    competitionId: string,
    importId: string,
    rowNumber: number,
    body: EditJudgeImportRowRequest,
  ): Observable<JudgeImportRow> {
    return this.apiClient.put<JudgeImportRow>(
      `/competitions/${competitionId}/judge-imports/${importId}/rows/${rowNumber}`,
      body,
    );
  }

  excludeRow(
    competitionId: string,
    importId: string,
    rowNumber: number,
  ): Observable<JudgeImportRow> {
    return this.apiClient.post<JudgeImportRow>(
      `/competitions/${competitionId}/judge-imports/${importId}/rows/${rowNumber}/exclude`,
      {},
    );
  }

  consolidate(competitionId: string, importId: string): Observable<JudgeImportConsolidateResult> {
    return this.apiClient.post<JudgeImportConsolidateResult>(
      `/competitions/${competitionId}/judge-imports/${importId}/consolidate`,
      {},
    );
  }
}
