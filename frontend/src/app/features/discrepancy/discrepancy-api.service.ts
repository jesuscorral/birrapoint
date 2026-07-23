import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

import { ApiClient } from '../../core/api/api-client.service';
import type { EvaluationComments, EvaluationScores } from '../../core/offline/db';

// GET /me/tables/{tableId}/discrepancies response shape (contracts/rest-api.md §Judge workspace,
// FR-031/FR-032). `evaluationId` identifies the caller's own row (`isMine`) so the adjust form
// below knows which evaluation to PUT.
export interface DiscrepancyTotal {
  judgeDisplayName: string;
  total: number;
  isMine: boolean;
  evaluationId: string;
}

export interface DiscrepancyView {
  alertId: string;
  blindCode: string;
  totals: DiscrepancyTotal[];
}

// PUT /me/tables/{tableId}/evaluations/{evaluationId} response shape (contracts/rest-api.md).
// `discrepancy` is null once every total for the sample has converged within 7 points (FR-032).
export interface AdjustEvaluationResult {
  evaluationId: string;
  status: 'Confirmed' | 'PendingConsensus';
  total: number;
  discrepancy: DiscrepancyView | null;
}

// US11/T082: judge-facing discrepancy consensus API. Deliberately NOT routed through
// SyncService/Dexie outbox — this repair flow assumes connectivity (the spec frames it as "shown
// to each involved judge as soon as they are next online"), so a failed PUT here surfaces an
// error to retry rather than enqueueing offline.
@Injectable({ providedIn: 'root' })
export class DiscrepancyApiService {
  private readonly apiClient = inject(ApiClient);

  getDiscrepancies(tableId: string): Observable<DiscrepancyView[]> {
    return this.apiClient.get<DiscrepancyView[]>(`/me/tables/${tableId}/discrepancies`);
  }

  adjustEvaluation(
    tableId: string,
    evaluationId: string,
    scores: EvaluationScores,
    comments: EvaluationComments,
  ): Observable<AdjustEvaluationResult> {
    return this.apiClient.put<AdjustEvaluationResult>(
      `/me/tables/${tableId}/evaluations/${evaluationId}`,
      { scores, comments },
    );
  }
}
