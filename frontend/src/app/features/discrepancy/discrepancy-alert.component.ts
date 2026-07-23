import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import type { Subscription } from 'rxjs';
import { filter } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import type { EvaluationComments, EvaluationScores } from '../../core/offline/db';
import { CompetitionHubService } from '../../core/realtime/competition-hub.service';
import type {
  DiscrepancyRaisedEvent,
  DiscrepancyResolvedEvent,
} from '../../core/realtime/competition-hub.events';
import { DiscrepancyApiService } from './discrepancy-api.service';
import type { DiscrepancyView } from './discrepancy-api.service';

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

function errorMessage(error: ApiError): string {
  return error.detail ?? error.title;
}

// Same caps/minimum as evaluation-sheet.component.ts (FR-023/FR-025) — duplicated rather than
// coupled, per this codebase's "unrelated slices don't share code" convention (see
// CorrectEvaluationCommandValidator's doc comment on the backend for the same rule).
const MIN_COMMENT_LENGTH = 20;

interface AdjustSectionConfig {
  key: keyof EvaluationScores;
  label: string;
  max: number;
  scoreControl:
    'aromaScore' | 'appearanceScore' | 'flavorScore' | 'mouthfeelScore' | 'overallScore';
  commentControl:
    'aromaComment' | 'appearanceComment' | 'flavorComment' | 'mouthfeelComment' | 'overallComment';
}

const SECTIONS: AdjustSectionConfig[] = [
  {
    key: 'aroma',
    label: 'Aroma',
    max: 12,
    scoreControl: 'aromaScore',
    commentControl: 'aromaComment',
  },
  {
    key: 'appearance',
    label: 'Appearance',
    max: 3,
    scoreControl: 'appearanceScore',
    commentControl: 'appearanceComment',
  },
  {
    key: 'flavor',
    label: 'Flavor',
    max: 20,
    scoreControl: 'flavorScore',
    commentControl: 'flavorComment',
  },
  {
    key: 'mouthfeel',
    label: 'Mouthfeel',
    max: 5,
    scoreControl: 'mouthfeelScore',
    commentControl: 'mouthfeelComment',
  },
  {
    key: 'overall',
    label: 'Overall Impression',
    max: 10,
    scoreControl: 'overallScore',
    commentControl: 'overallComment',
  },
];

function buildAdjustForm(): FormGroup {
  const group: Record<string, FormControl> = {};
  for (const section of SECTIONS) {
    group[section.scoreControl] = new FormControl<number | null>(null, {
      validators: [Validators.required, Validators.min(0), Validators.max(section.max)],
    });
    group[section.commentControl] = new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(MIN_COMMENT_LENGTH)],
    });
  }
  return new FormGroup(group);
}

function buildPayload(form: FormGroup): { scores: EvaluationScores; comments: EvaluationComments } {
  const raw = form.getRawValue() as Record<string, number | string>;
  return {
    scores: {
      aroma: raw['aromaScore'] as number,
      appearance: raw['appearanceScore'] as number,
      flavor: raw['flavorScore'] as number,
      mouthfeel: raw['mouthfeelScore'] as number,
      overall: raw['overallScore'] as number,
    },
    comments: {
      aroma: raw['aromaComment'] as string,
      appearance: raw['appearanceComment'] as string,
      flavor: raw['flavorComment'] as string,
      mouthfeel: raw['mouthfeelComment'] as string,
      overall: raw['overallComment'] as string,
    },
  };
}

interface AdjustFormState {
  form: FormGroup;
  submitting: boolean;
  error: string | null;
}

// T082/US11: judge-facing discrepancy consensus screen (FR-031/FR-032). Only ever renders
// blindCode + judgeDisplayName/total from DiscrepancyView — the BR-01/FR-019 anonymity boundary
// — never an entrant field. The adjustment PUT assumes connectivity (see
// DiscrepancyApiService's doc comment); it is not routed through the offline outbox.
@Component({
  selector: 'app-discrepancy-alert',
  imports: [ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p><a [routerLink]="['/judge', 'tables', tableId]">&larr; Back to table</a></p>
    <h1>Discrepancy alerts</h1>

    @if (loadError(); as message) {
      <p role="alert">{{ message }}</p>
    }

    @if (!loadError()) {
      @for (resolved of resolvedAlerts(); track resolved.alertId) {
        <div class="alert-card alert-card--resolved" role="status">
          <h2>{{ resolved.blindCode }}</h2>
          <p>Resolved — your evaluation is confirmed.</p>
        </div>
      }

      @if (alerts().length === 0) {
        <p>
          No open discrepancies on this table.
          <a [routerLink]="['/judge', 'tables', tableId]">Back to table</a>
        </p>
      }

      @for (alert of alerts(); track alert.alertId) {
        <div class="alert-card">
          <h2>{{ alert.blindCode }}</h2>
          <table class="totals-table">
            <thead>
              <tr>
                <th scope="col">Judge</th>
                <th scope="col">Total</th>
              </tr>
            </thead>
            <tbody>
              @for (total of alert.totals; track total.evaluationId) {
                <tr>
                  <td>{{ total.judgeDisplayName }}{{ total.isMine ? ' (you)' : '' }}</td>
                  <td>{{ total.total }}</td>
                </tr>
              }
            </tbody>
          </table>

          @if (mineEvaluationId(alert); as evaluationId) {
            @if (!isExpanded(alert.alertId)) {
              <button type="button" (click)="onAdjustClick(alert.alertId)">
                Adjust my evaluation
              </button>
            } @else {
              <form [formGroup]="formFor(alert.alertId)" (ngSubmit)="onAdjustSubmit(alert)">
                @for (section of sections; track section.key) {
                  <fieldset class="evaluation-section">
                    <legend>{{ section.label }} (0–{{ section.max }})</legend>

                    <label>
                      Score
                      <input
                        type="number"
                        [min]="0"
                        [max]="section.max"
                        [formControlName]="section.scoreControl"
                      />
                    </label>

                    <label>
                      Comment
                      <textarea [formControlName]="section.commentControl" rows="3"></textarea>
                    </label>
                    <p class="comment-hint">
                      @if (remainingChars(alert.alertId, section.key) > 0) {
                        {{ remainingChars(alert.alertId, section.key) }} more character{{
                          remainingChars(alert.alertId, section.key) === 1 ? '' : 's'
                        }}
                        needed (minimum {{ minCommentLength }}).
                      } @else {
                        Minimum length met.
                      }
                    </p>
                  </fieldset>
                }

                @if (adjustError(alert.alertId); as message) {
                  <p role="alert">{{ message }}</p>
                }

                <button
                  type="submit"
                  [disabled]="formFor(alert.alertId).invalid || isSubmitting(alert.alertId)"
                >
                  Submit adjustment
                </button>
                <button
                  type="button"
                  [disabled]="isSubmitting(alert.alertId)"
                  (click)="onCancelAdjust(alert.alertId)"
                >
                  Cancel
                </button>
              </form>
            }
          }
        </div>
      }
    }
  `,
  styles: `
    .alert-card {
      border: 1px solid #d1d5db;
      border-radius: 0.5rem;
      padding: 1rem;
      margin: 1rem 0;
    }

    .alert-card--resolved {
      border-color: #86efac;
      background: #f0fdf4;
    }

    .totals-table {
      width: 100%;
      border-collapse: collapse;
      margin: 0.5rem 0;
    }

    .totals-table th,
    .totals-table td {
      text-align: left;
      padding: 0.25rem 0.5rem;
      border-bottom: 1px solid #e5e7eb;
    }

    .evaluation-section {
      border: 1px solid #d1d5db;
      border-radius: 0.5rem;
      padding: 0.75rem 1rem;
      margin: 1rem 0;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }

    .comment-hint {
      margin: 0;
      font-size: 0.85rem;
      color: #6b7280;
    }
  `,
})
export class DiscrepancyAlertComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(DiscrepancyApiService);
  private readonly hub = inject(CompetitionHubService);

  protected readonly tableId = this.route.snapshot.paramMap.get('tableId')!;
  protected readonly sections = SECTIONS;
  protected readonly minCommentLength = MIN_COMMENT_LENGTH;

  protected readonly alerts = signal<DiscrepancyView[]>([]);
  protected readonly resolvedAlerts = signal<{ alertId: string; blindCode: string }[]>([]);
  protected readonly loadError = signal<string | null>(null);

  private readonly adjustForms = signal<ReadonlyMap<string, AdjustFormState>>(new Map());

  private discrepancyRaisedSubscription: Subscription | null = null;
  private discrepancyResolvedSubscription: Subscription | null = null;

  constructor() {
    this.loadDiscrepancies();
  }

  ngOnInit(): void {
    void this.connectToHub();
  }

  ngOnDestroy(): void {
    this.discrepancyRaisedSubscription?.unsubscribe();
    this.discrepancyResolvedSubscription?.unsubscribe();
    void this.hub.leaveTable(this.tableId).catch(() => {
      // Best-effort: leaving on navigation-away is a courtesy, not a functional requirement.
    });
  }

  protected mineEvaluationId(alert: DiscrepancyView): string | null {
    return alert.totals.find((total) => total.isMine)?.evaluationId ?? null;
  }

  protected isExpanded(alertId: string): boolean {
    return this.adjustForms().has(alertId);
  }

  protected formFor(alertId: string): FormGroup {
    return this.adjustForms().get(alertId)!.form;
  }

  protected isSubmitting(alertId: string): boolean {
    return this.adjustForms().get(alertId)?.submitting ?? false;
  }

  protected adjustError(alertId: string): string | null {
    return this.adjustForms().get(alertId)?.error ?? null;
  }

  protected remainingChars(alertId: string, key: AdjustSectionConfig['key']): number {
    const section = SECTIONS.find((s) => s.key === key)!;
    const value = (this.formFor(alertId).get(section.commentControl)?.value as string) ?? '';
    return Math.max(0, MIN_COMMENT_LENGTH - value.length);
  }

  protected onAdjustClick(alertId: string): void {
    this.adjustForms.update((current) => {
      const next = new Map(current);
      next.set(alertId, { form: buildAdjustForm(), submitting: false, error: null });
      return next;
    });
  }

  protected onCancelAdjust(alertId: string): void {
    this.adjustForms.update((current) => {
      const next = new Map(current);
      next.delete(alertId);
      return next;
    });
  }

  protected onAdjustSubmit(alert: DiscrepancyView): void {
    const state = this.adjustForms().get(alert.alertId);
    if (!state || state.form.invalid || state.submitting) {
      return;
    }
    const mine = alert.totals.find((total) => total.isMine);
    if (!mine) {
      return;
    }

    this.patchFormState(alert.alertId, { submitting: true, error: null });

    const { scores, comments } = buildPayload(state.form);
    this.api.adjustEvaluation(this.tableId, mine.evaluationId, scores, comments).subscribe({
      next: (result) => {
        if (result.discrepancy === null || result.status === 'Confirmed') {
          this.markResolved(alert.alertId, alert.blindCode);
        } else {
          this.replaceAlert(result.discrepancy);
          this.onCancelAdjust(alert.alertId);
        }
      },
      error: (error: unknown) => {
        this.patchFormState(alert.alertId, {
          submitting: false,
          error: this.describeAdjustError(error),
        });
      },
    });
  }

  private patchFormState(
    alertId: string,
    patch: Partial<Pick<AdjustFormState, 'submitting' | 'error'>>,
  ): void {
    this.adjustForms.update((current) => {
      const existing = current.get(alertId);
      if (!existing) {
        return current;
      }
      const next = new Map(current);
      next.set(alertId, { ...existing, ...patch });
      return next;
    });
  }

  private markResolved(alertId: string, blindCode: string): void {
    this.alerts.update((current) => current.filter((alert) => alert.alertId !== alertId));
    this.resolvedAlerts.update((current) => [...current, { alertId, blindCode }]);
    this.adjustForms.update((current) => {
      const next = new Map(current);
      next.delete(alertId);
      return next;
    });
  }

  private replaceAlert(discrepancy: DiscrepancyView): void {
    this.alerts.update((current) =>
      current.map((alert) => (alert.alertId === discrepancy.alertId ? discrepancy : alert)),
    );
  }

  private describeAdjustError(error: unknown): string {
    const apiError = toGenericApiError(error);
    if (apiError.urn === 'urn:birrapoint:evaluation-locked') {
      return 'This evaluation is no longer open for adjustment — it may already have been resolved.';
    }
    return errorMessage(apiError);
  }

  private async connectToHub(): Promise<void> {
    try {
      await this.hub.start();
      await this.hub.joinTable(this.tableId);
      this.discrepancyRaisedSubscription = this.hub
        .on('DiscrepancyRaised')
        .pipe(filter((event: DiscrepancyRaisedEvent) => event.tableId === this.tableId))
        .subscribe(() => this.loadDiscrepancies());
      this.discrepancyResolvedSubscription = this.hub
        .on('DiscrepancyResolved')
        .pipe(filter((event: DiscrepancyResolvedEvent) => event.tableId === this.tableId))
        .subscribe(() => this.loadDiscrepancies());
    } catch {
      // Realtime is a best-effort notification channel (contracts/signalr-hub.md): this view
      // stays fully functional over REST without it — a judge just won't see another judge's
      // adjustment live and will pick it up on next load instead.
    }
  }

  private loadDiscrepancies(): void {
    this.loadError.set(null);
    this.api.getDiscrepancies(this.tableId).subscribe({
      next: (alerts) => this.alerts.set(alerts),
      error: (error: unknown) => this.loadError.set(errorMessage(toGenericApiError(error))),
    });
  }
}
