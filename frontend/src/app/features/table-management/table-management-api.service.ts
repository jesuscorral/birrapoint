import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

import { ApiClient } from '../../core/api/api-client.service';

export type TableState = 'Open' | 'Closed';

export interface TableJudge {
  id: string;
  email: string;
  displayName: string;
}

export interface TableSample {
  beerEntryId: string;
  blindCode: string;
  styleCode: string;
  styleName: string;
  abvLow: number | null;
  abvHigh: number | null;
  notValidForBos: boolean;
}

export interface TableProgress {
  submitted: number;
  total: number;
}

export interface TableStats {
  meanAbv: number | null;
  styleCount: number;
  styles: string[];
}

// GET /tables shape (contracts/rest-api.md §Tables).
export interface TableSummary {
  id: string;
  name: string;
  state: TableState;
  judges: TableJudge[];
  samples: TableSample[];
  progress: TableProgress;
  stats: TableStats;
}

// POST/PUT response: the table plus entries newly flagged Not Valid for BOS by this call (FR-018).
export interface TableMutationResult extends TableSummary {
  bosFlaggedEntryIds: string[];
}

// GET /entries shape — tastingTableId/tastingTableName are null when unassigned.
export interface EntryListItem {
  id: string;
  blindCode: string;
  styleCode: string;
  styleName: string;
  abvLow: number | null;
  abvHigh: number | null;
  beerName: string;
  notValidForBos: boolean;
  tastingTableId: string | null;
  tastingTableName: string | null;
}

export interface JudgeListItem {
  id: string;
  email: string;
  displayName: string;
}

// Full-desired-state body for POST/PUT — replaces the table's entire judge/entry set each call.
export interface TableAssignmentRequest {
  name: string;
  judgeIds: string[];
  beerEntryIds: string[];
}

@Injectable({ providedIn: 'root' })
export class TableManagementApiService {
  private readonly apiClient = inject(ApiClient);

  getTables(competitionId: string): Observable<TableSummary[]> {
    return this.apiClient.get<TableSummary[]>(`/competitions/${competitionId}/tables`);
  }

  getEntries(competitionId: string): Observable<EntryListItem[]> {
    return this.apiClient.get<EntryListItem[]>(`/competitions/${competitionId}/entries`);
  }

  getJudges(competitionId: string): Observable<JudgeListItem[]> {
    return this.apiClient.get<JudgeListItem[]>(`/competitions/${competitionId}/judges`);
  }

  createTable(
    competitionId: string,
    request: TableAssignmentRequest,
  ): Observable<TableMutationResult> {
    return this.apiClient.post<TableMutationResult>(
      `/competitions/${competitionId}/tables`,
      request,
    );
  }

  updateTable(
    competitionId: string,
    tableId: string,
    request: TableAssignmentRequest,
  ): Observable<TableMutationResult> {
    return this.apiClient.put<TableMutationResult>(
      `/competitions/${competitionId}/tables/${tableId}`,
      request,
    );
  }
}
