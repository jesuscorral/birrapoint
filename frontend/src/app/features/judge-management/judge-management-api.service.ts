import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';

import { ApiClient } from '../../core/api/api-client.service';

export type InvitationStatus = 'Pending' | 'Sent' | 'Failed';

// Wire shape `reason` on JudgeSkipDto (contracts/rest-api.md §Judges).
export type JudgeSkipReason = 'duplicate-in-list' | 'already-registered';

export interface CreatedJudge {
  id: string;
  email: string;
}

export interface JudgeSkip {
  email: string;
  reason: JudgeSkipReason;
}

export interface RegisterJudgesResult {
  created: CreatedJudge[];
  skipped: JudgeSkip[];
}

export interface JudgeProfile {
  id: string;
  email: string;
  displayName: string;
  invitationStatus: InvitationStatus;
  attempts: number;
  lastError: string | null;
  sentAt: string | null;
}

@Injectable({ providedIn: 'root' })
export class JudgeManagementApiService {
  private readonly apiClient = inject(ApiClient);

  registerJudges(competitionId: string, emails: string[]): Observable<RegisterJudgesResult> {
    return this.apiClient.post<RegisterJudgesResult>(`/competitions/${competitionId}/judges`, {
      emails,
    });
  }

  getJudges(competitionId: string): Observable<JudgeProfile[]> {
    return this.apiClient.get<JudgeProfile[]>(`/competitions/${competitionId}/judges`);
  }

  updateJudgeEmail(
    competitionId: string,
    judgeId: string,
    email: string,
  ): Observable<JudgeProfile> {
    return this.apiClient.put<JudgeProfile>(`/competitions/${competitionId}/judges/${judgeId}`, {
      email,
    });
  }

  resendInvitation(
    competitionId: string,
    judgeId: string,
  ): Observable<{ status: InvitationStatus }> {
    return this.apiClient.post<{ status: InvitationStatus }>(
      `/competitions/${competitionId}/judges/${judgeId}/invitation`,
      {},
    );
  }
}
