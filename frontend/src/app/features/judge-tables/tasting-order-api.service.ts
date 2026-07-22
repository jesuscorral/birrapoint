import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

import { ApiClient } from '../../core/api/api-client.service';

// GET /me/tables shape (contracts/rest-api.md §Judge workspace).
export interface JudgeTableSummary {
  tableId: string;
  name: string;
  competitionState: string;
  tableState: string;
  orderFixed: boolean;
  orderFixedBy: string | null;
}

export type EvaluationStatus = 'NotStarted' | 'Submitted' | 'PendingConsensus';

// POST /me/tables/{tableId}/close success response (contracts/rest-api.md §Judge workspace,
// FR-033/FR-042): per-blind-code consolidated mean, computed once every active judge's
// evaluation is in.
export interface CloseTableResponse {
  consolidatedScores: { blindCode: string; mean: number }[];
}

// GET /me/tables/{tableId}/samples and POST /me/tables/{tableId}/order response shape
// (contracts/rest-api.md §Judge workspace). This is the BR-01/FR-019 anonymity boundary
// (data-model.md §Anonymity boundary) on the frontend side of the wire: the backend's
// JudgeSampleDto structurally never carries beerName/participant/brewery/origin, and this
// interface — and every judge-facing template that renders it — MUST NOT gain a field beyond
// this exact list, regardless of what convenience a future backend DTO might add.
export interface JudgeSample {
  beerEntryId: string;
  blindCode: string;
  styleCode: string;
  styleName: string;
  sequenceOrder: number | null;
  evaluationStatus: EvaluationStatus;
}

@Injectable({ providedIn: 'root' })
export class TastingOrderApiService {
  private readonly apiClient = inject(ApiClient);

  getMyTables(): Observable<JudgeTableSummary[]> {
    return this.apiClient.get<JudgeTableSummary[]>('/me/tables');
  }

  getTableSamples(tableId: string): Observable<JudgeSample[]> {
    return this.apiClient.get<JudgeSample[]>(`/me/tables/${tableId}/samples`);
  }

  // Body must be an exact permutation of the table's current sample beerEntryIds
  // (contracts/rest-api.md); the backend validates this and 400s otherwise.
  fixOrder(tableId: string, orderedBeerEntryIds: string[]): Observable<JudgeSample[]> {
    return this.apiClient.post<JudgeSample[]>(`/me/tables/${tableId}/order`, {
      orderedBeerEntryIds,
    });
  }

  // No request body (contracts/rest-api.md `POST /me/tables/{tableId}/close`); FR-033, one-shot
  // and irreversible.
  closeTable(tableId: string): Observable<CloseTableResponse> {
    return this.apiClient.post<CloseTableResponse>(`/me/tables/${tableId}/close`, null);
  }
}
