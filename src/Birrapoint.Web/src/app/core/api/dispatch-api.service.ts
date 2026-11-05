import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { from, map, of, switchMap } from 'rxjs';

import { ApiClient } from './api-client.service';
import { blobToText } from './blob-text';

// GET /competitions/{id}/dispatch response shape (contracts/rest-api.md §Results & dispatch,
// US10/FR-041) — per-participant result-email delivery status.
export interface DispatchStatusRow {
  participantId: string;
  email: string;
  status: string;
  attempts: number;
  lastError: string | null;
}

// GET /competitions/{id}/results/archive: 200 with the ZIP bytes once BundleZip has completed, or
// 202 with { status } (the in-progress DispatchJob status) while it hasn't. Both arrive through
// the same blob-typed request, so "not ready yet" is modeled as a value, not a thrown error.
export type ResultsArchiveResult = { ready: true; blob: Blob } | { ready: false; status: string };

function parseNotReadyStatus(text: string): string {
  try {
    const parsed = JSON.parse(text) as { status?: string };
    return parsed.status ?? 'Unknown';
  } catch {
    return 'Unknown';
  }
}

@Injectable({ providedIn: 'root' })
export class DispatchApiService {
  private readonly apiClient = inject(ApiClient);

  getDispatchStatus(competitionId: string): Observable<DispatchStatusRow[]> {
    return this.apiClient.get<DispatchStatusRow[]>(`/competitions/${competitionId}/dispatch`);
  }

  retryDispatch(competitionId: string, participantIds: string[]): Observable<void> {
    return this.apiClient.post<void>(`/competitions/${competitionId}/dispatch/retries`, {
      participantIds,
    });
  }

  downloadResultsArchive(competitionId: string): Observable<ResultsArchiveResult> {
    return this.apiClient.getBlob(`/competitions/${competitionId}/results/archive`).pipe(
      switchMap((response) => {
        if (response.status === 200 && response.body) {
          return of<ResultsArchiveResult>({ ready: true, blob: response.body });
        }
        if (!response.body) {
          return of<ResultsArchiveResult>({ ready: false, status: 'Unknown' });
        }
        return from(blobToText(response.body)).pipe(
          map(
            (text) => ({ ready: false, status: parseNotReadyStatus(text) }) as ResultsArchiveResult,
          ),
        );
      }),
    );
  }
}
