import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

import { ApiClient } from './api-client.service';

// GET /competitions/{id}/progress response shape (contracts/rest-api.md §Monitoring, US9).
export interface TableProgressSummary {
  tableId: string;
  name: string;
  state: 'Open' | 'Closed';
  completed: number;
  expected: number;
  percent: number;
}

export interface EvaluationAuditItem {
  judgeDisplayName: string;
  scores: { aroma: number; appearance: number; flavor: number; mouthfeel: number; overall: number };
  comments: {
    aroma: string;
    appearance: string;
    flavor: string;
    mouthfeel: string;
    overall: string;
  };
  total: number;
  status: string;
}

// GET /competitions/{id}/entries/{entryId}/evaluations response shape — the read-only audit
// drill-down (FR-038). consolidatedMean is null until the owning table has closed.
export interface EntryEvaluationsResult {
  blindCode: string;
  evaluations: EvaluationAuditItem[];
  consolidatedMean: number | null;
}

@Injectable({ providedIn: 'root' })
export class MonitoringApiService {
  private readonly apiClient = inject(ApiClient);

  getProgress(competitionId: string): Observable<TableProgressSummary[]> {
    return this.apiClient.get<TableProgressSummary[]>(`/competitions/${competitionId}/progress`);
  }

  getEntryEvaluations(competitionId: string, entryId: string): Observable<EntryEvaluationsResult> {
    return this.apiClient.get<EntryEvaluationsResult>(
      `/competitions/${competitionId}/entries/${entryId}/evaluations`,
    );
  }
}
