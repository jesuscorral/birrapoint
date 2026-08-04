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
  // Populated from a judge-roster import row (T118/US14); null for judges created via the plain
  // email-list registration flow above.
  bjcpRank: string | null;
  bjcpId: string | null;
  preferredCategory: string | null;
  preferences: string | null;
  invitationStatus: InvitationStatus;
  attempts: number;
  lastError: string | null;
  sentAt: string | null;
}

export interface NotifyJudgesResult {
  queued: CreatedJudge[];
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

  // FR-059: bulk "Notify judges" action, decoupled from both provisioning paths (plain email-list
  // registration and judge-roster import consolidation), neither of which sends an invitation.
  notifyJudges(competitionId: string): Observable<NotifyJudgesResult> {
    return this.apiClient.post<NotifyJudgesResult>(
      `/competitions/${competitionId}/judges/notify`,
      {},
    );
  }
}
