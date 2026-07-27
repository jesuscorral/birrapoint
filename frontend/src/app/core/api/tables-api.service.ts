import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

import { ApiClient } from './api-client.service';

// GET /tables shape (contracts/rest-api.md §Tables) — minimal subset consumed here (judges per
// table), mirroring features/table-management/table-management-api.service.ts's TableJudge/
// TableSummary wire shape rather than importing it: core/ must not depend on features/, and
// table-management keeps its own full copy (samples/progress/stats) for its CRUD flow.
export interface TableJudge {
  id: string;
  email: string;
  displayName: string;
}

export interface TableSummary {
  id: string;
  name: string;
  judges: TableJudge[];
}

export interface RemoveJudgeResult {
  tableId: string;
  judgeId: string;
}

// core/api/ home for table data consumed by a second feature (dashboard, T087) alongside
// table-management — same "≥2 features -> core/api/" rule already applied to
// CompetitionsApiService/EntriesApiService (Docs/arquitectura_viva.md).
@Injectable({ providedIn: 'root' })
export class TablesApiService {
  private readonly apiClient = inject(ApiClient);

  getTables(competitionId: string): Observable<TableSummary[]> {
    return this.apiClient.get<TableSummary[]>(`/competitions/${competitionId}/tables`);
  }

  // Live judge removal (FR-039/US12): 200 on success, 404 if not found/not owned (never reveals
  // existence).
  removeJudge(
    competitionId: string,
    tableId: string,
    judgeId: string,
  ): Observable<RemoveJudgeResult> {
    return this.apiClient.delete<RemoveJudgeResult>(
      `/competitions/${competitionId}/tables/${tableId}/judges/${judgeId}`,
    );
  }
}
