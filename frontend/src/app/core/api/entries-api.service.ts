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
  abvLow: number | null;
  abvHigh: number | null;
  beerName: string;
  notValidForBos: boolean;
  tastingTableId: string | null;
  tastingTableName: string | null;
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
