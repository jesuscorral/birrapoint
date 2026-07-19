import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

import { ApiClient } from '../../core/api/api-client.service';

export type CompetitionState = 'Draft' | 'Active' | 'InEvaluation' | 'Finalized';

// Wire body for POST/PUT /competitions (contracts/rest-api.md §Competitions). Dates are DateOnly
// server-side, serialized as "YYYY-MM-DD".
export interface CompetitionPayload {
  name: string;
  venue: string;
  startDate: string;
  endDate: string;
  description?: string;
  logoUrl?: string;
  entryLimit?: number;
  registrationStart?: string;
  registrationEnd?: string;
}

export interface CompetitionDetail {
  id: string;
  name: string;
  venue: string;
  startDate: string;
  endDate: string;
  description: string | null;
  logoUrl: string | null;
  entryLimit: number | null;
  registrationStart: string | null;
  registrationEnd: string | null;
  state: CompetitionState;
}

@Injectable({ providedIn: 'root' })
export class CompetitionsApiService {
  private readonly apiClient = inject(ApiClient);

  create(payload: CompetitionPayload): Observable<CompetitionDetail> {
    return this.apiClient.post<CompetitionDetail>('/competitions', payload);
  }

  update(id: string, payload: CompetitionPayload): Observable<CompetitionDetail> {
    return this.apiClient.put<CompetitionDetail>(`/competitions/${id}`, payload);
  }

  getById(id: string): Observable<CompetitionDetail> {
    return this.apiClient.get<CompetitionDetail>(`/competitions/${id}`);
  }
}
