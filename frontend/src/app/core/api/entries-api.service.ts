import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

import { ApiClient } from './api-client.service';

// GET /entries shape (contracts/rest-api.md §Import) — tastingTableId/tastingTableName are null
// when unassigned.
export interface EntryListItem {
  id: string;
  blindCode: string;
  styleCode: string;
  styleName: string;
  // Real ABV% of this specific beer entry (distinct from abvLow/abvHigh, the BJCP style's
  // declared range).
  abvPercent: number;
  abvLow: number | null;
  abvHigh: number | null;
  beerName: string;
  notValidForBos: boolean;
  tastingTableId: string | null;
  tastingTableName: string | null;
  // T124: the organizer-defined competition category (wizard step 3) this entry was imported
  // under — null for entries created outside the import flow.
  competitionCategoryName: string | null;
  // T124: the BJCP taxonomy's own category (e.g. "21"/"IPA") — an independent axis from
  // competitionCategoryName; null only when styleCode has no catalog row.
  bjcpCategoryNumber: string | null;
  bjcpCategoryName: string | null;
}

// Promoted out of table-management-api.service (same reasoning as CompetitionsApiService/
// CatalogApiService): consumed by table-management and, now, the monitoring dashboard.
@Injectable({ providedIn: 'root' })
export class EntriesApiService {
  private readonly apiClient = inject(ApiClient);

  getEntries(competitionId: string): Observable<EntryListItem[]> {
    return this.apiClient.get<EntryListItem[]>(`/competitions/${competitionId}/entries`);
  }
}
