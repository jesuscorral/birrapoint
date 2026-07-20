import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';

import { ApiError } from '../../core/api/api-error';
import { JudgeManagementApiService } from './judge-management-api.service';
import type { JudgeProfile, JudgeSkip, RegisterJudgesResult } from './judge-management-api.service';

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

function errorMessage(error: ApiError): string {
  return error.detail ?? error.title;
}

const SKIP_REASON_LABELS: Record<JudgeSkip['reason'], string> = {
  'duplicate-in-list': 'duplicate in the pasted list',
  'already-registered': 'already registered',
};

function skipReasonLabel(reason: JudgeSkip['reason']): string {
  return SKIP_REASON_LABELS[reason];
}

function splitEmails(raw: string): string[] {
  return raw
    .split(/[\n,]/)
    .map((email) => email.trim())
    .filter((email) => email.length > 0);
}

// T043: paste-list registration -> created/skipped report -> delivery status table, kept as one
// signal-driven component (no stepper) since the registration form and the status list are both
// always visible together, mirroring the entry-import feature's shape.
@Component({
  selector: 'app-judge-management',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Judge management</h1>

    <section aria-label="Register judges">
      <h2>Register judges</h2>
      <label>
        Judge emails (one per line, or comma-separated)
        <textarea [(ngModel)]="emailsInput" rows="5" name="emails"></textarea>
      </label>
      @if (registerError(); as message) {
        <p role="alert">{{ message }}</p>
      }
      <button
        type="button"
        [disabled]="!emailsInput().trim() || registering()"
        (click)="onRegister()"
      >
        Register judges
      </button>

      @if (registerResult(); as result) {
        <div aria-label="Registration report">
          @if (result.created.length > 0) {
            <p>Created:</p>
            <ul>
              @for (judge of result.created; track judge.id) {
                <li>{{ judge.email }}</li>
              }
            </ul>
          }
          @if (result.skipped.length > 0) {
            <p>Skipped:</p>
            <ul>
              @for (skip of result.skipped; track skip.email) {
                <li>{{ skip.email }} — {{ skipReasonLabel(skip.reason) }}</li>
              }
            </ul>
          }
        </div>
      }
    </section>

    <section aria-label="Delivery status">
      <h2>Delivery status</h2>
      @if (listError(); as message) {
        <p role="alert">{{ message }}</p>
      }
      <table>
        <thead>
          <tr>
            <th scope="col">Email</th>
            <th scope="col">Name</th>
            <th scope="col">Invitation status</th>
            <th scope="col">Attempts</th>
            <th scope="col">Last error</th>
            <th scope="col">Actions</th>
          </tr>
        </thead>
        <tbody>
          @for (judge of judges(); track judge.id) {
            <tr [attr.data-judge-email]="judge.email">
              <td>{{ judge.email }}</td>
              <td>{{ judge.displayName }}</td>
              <td>{{ judge.invitationStatus }}</td>
              <td>{{ judge.attempts }}</td>
              <td>{{ judge.lastError }}</td>
              <td>
                <button type="button" [disabled]="isBusy(judge.id)" (click)="onResend(judge.id)">
                  Resend invitation
                </button>
                @if (editingJudgeId() === judge.id) {
                  <input type="email" [(ngModel)]="editEmailInput" name="editEmail" />
                  <button
                    type="button"
                    [disabled]="isBusy(judge.id)"
                    (click)="onSaveEmail(judge.id)"
                  >
                    Save
                  </button>
                  <button type="button" (click)="onCancelEdit()">Cancel</button>
                  @if (editError(); as message) {
                    <p role="alert">{{ message }}</p>
                  }
                } @else {
                  <button type="button" (click)="onStartEdit(judge)">Edit email</button>
                }
              </td>
            </tr>
          }
        </tbody>
      </table>
    </section>
  `,
})
export class JudgeManagementComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(JudgeManagementApiService);

  private readonly competitionId = this.route.snapshot.paramMap.get('id')!;

  protected readonly emailsInput = signal('');
  protected readonly registering = signal(false);
  protected readonly registerError = signal<string | null>(null);
  protected readonly registerResult = signal<RegisterJudgesResult | null>(null);

  protected readonly judges = signal<JudgeProfile[]>([]);
  protected readonly listError = signal<string | null>(null);

  protected readonly busyJudgeId = signal<string | null>(null);
  protected readonly editingJudgeId = signal<string | null>(null);
  protected readonly editEmailInput = signal('');
  protected readonly editError = signal<string | null>(null);

  protected readonly skipReasonLabel = skipReasonLabel;

  constructor() {
    this.loadJudges();
  }

  protected isBusy(judgeId: string): boolean {
    return this.busyJudgeId() === judgeId;
  }

  private loadJudges(): void {
    this.api.getJudges(this.competitionId).subscribe({
      next: (judges) => this.judges.set(judges),
      error: (error: unknown) => {
        this.listError.set(errorMessage(toGenericApiError(error)));
      },
    });
  }

  protected onRegister(): void {
    const emails = splitEmails(this.emailsInput());
    if (emails.length === 0 || this.registering()) {
      return;
    }

    this.registering.set(true);
    this.registerError.set(null);

    this.api.registerJudges(this.competitionId, emails).subscribe({
      next: (result) => {
        this.registering.set(false);
        this.registerResult.set(result);
        this.emailsInput.set('');
        this.loadJudges();
      },
      error: (error: unknown) => {
        this.registering.set(false);
        this.registerError.set(errorMessage(toGenericApiError(error)));
      },
    });
  }

  protected onResend(judgeId: string): void {
    if (this.isBusy(judgeId)) {
      return;
    }

    this.busyJudgeId.set(judgeId);
    this.listError.set(null);

    this.api.resendInvitation(this.competitionId, judgeId).subscribe({
      next: () => {
        this.busyJudgeId.set(null);
        this.loadJudges();
      },
      error: (error: unknown) => {
        this.busyJudgeId.set(null);
        this.listError.set(errorMessage(toGenericApiError(error)));
      },
    });
  }

  protected onStartEdit(judge: JudgeProfile): void {
    this.editingJudgeId.set(judge.id);
    this.editEmailInput.set(judge.email);
    this.editError.set(null);
  }

  protected onCancelEdit(): void {
    this.editingJudgeId.set(null);
    this.editError.set(null);
  }

  protected onSaveEmail(judgeId: string): void {
    if (this.isBusy(judgeId)) {
      return;
    }

    const email = this.editEmailInput().trim();
    if (!email) {
      return;
    }

    this.busyJudgeId.set(judgeId);
    this.editError.set(null);

    this.api.updateJudgeEmail(this.competitionId, judgeId, email).subscribe({
      next: () => {
        this.busyJudgeId.set(null);
        this.editingJudgeId.set(null);
        this.loadJudges();
      },
      error: (error: unknown) => {
        this.busyJudgeId.set(null);
        this.editError.set(errorMessage(toGenericApiError(error)));
      },
    });
  }
}
