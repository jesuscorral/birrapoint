import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { ApiError } from '../../core/api/api-error';
import type { EvaluationComments, EvaluationScores } from '../../core/offline/db';
import { SyncService } from '../../core/offline/sync.service';
import { StyleReferencePanelComponent } from './style-reference/style-reference-panel.component';
import { TastingOrderApiService } from '../judge-tables/tasting-order-api.service';
import type { JudgeSample } from '../judge-tables/tasting-order-api.service';

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

function errorMessage(error: ApiError): string {
  return error.detail ?? error.title;
}

// Best-effort cache of the last successfully-fetched sample per beerEntryId (US7 Acceptance
// Scenario 3/quickstart.md scenario 7: "restart the app [while offline], verify data intact").
// Deliberately a plain localStorage entry, not a Dexie table: this is read-only metadata
// (blindCode/style/evaluationStatus) refreshed on every successful mount, not something the
// offline engine (R-08, SyncService) needs to reconcile or replay — it only ever gets consulted
// as a fallback when the live fetch below can't reach the server at all.
const SAMPLE_CACHE_KEY_PREFIX = 'birrapoint:evaluation-sample:';

function cacheSample(sample: JudgeSample): void {
  try {
    localStorage.setItem(`${SAMPLE_CACHE_KEY_PREFIX}${sample.beerEntryId}`, JSON.stringify(sample));
  } catch {
    // Best-effort: a full/blocked localStorage only forfeits the offline-restart fallback below,
    // never today's live (online) rendering path.
  }
}

function readCachedSample(beerEntryId: string): JudgeSample | null {
  try {
    const raw = localStorage.getItem(`${SAMPLE_CACHE_KEY_PREFIX}${beerEntryId}`);
    return raw ? (JSON.parse(raw) as JudgeSample) : null;
  } catch {
    return null;
  }
}

// FR-023/FR-025 section caps and minimum comment length — must match SubmitEvaluationRules on
// the backend exactly (client-side enforcement here is defense-in-depth, not a replacement: the
// backend re-validates every submission regardless).
const MIN_COMMENT_LENGTH = 20;

interface EvaluationSectionConfig {
  key: keyof EvaluationScores;
  label: string;
  max: number;
  scoreControl:
    'aromaScore' | 'appearanceScore' | 'flavorScore' | 'mouthfeelScore' | 'overallScore';
  commentControl:
    'aromaComment' | 'appearanceComment' | 'flavorComment' | 'mouthfeelComment' | 'overallComment';
}

const SECTIONS: EvaluationSectionConfig[] = [
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

function buildForm(): FormGroup {
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

// T059/US7: the offline-first blind evaluation sheet (FR-022/FR-023/FR-025/FR-026/FR-027,
// SC-003). Judge-facing — only ever renders/handles blindCode + style (BR-01/FR-019); never an
// entrant field. All persistence (drafts, outbox, replay) is delegated to SyncService — this
// component never touches Dexie or the network directly.
@Component({
  selector: 'app-evaluation-sheet',
  imports: [ReactiveFormsModule, RouterLink, StyleReferencePanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p><a [routerLink]="['/judge', 'tables', tableId]">&larr; Back to table</a></p>

    @if (isOffline()) {
      <p role="status" class="offline-badge">Offline mode — data protected locally</p>
    }

    @if (loadError(); as message) {
      <p role="alert">{{ message }}</p>
    }

    @if (!loadError() && sample(); as currentSample) {
      <h1>{{ currentSample.blindCode }}</h1>
      <p class="sample-style">{{ currentSample.styleName }} ({{ currentSample.styleCode }})</p>

      <app-style-reference-panel
        [styleCode]="currentSample.styleCode"
        [styleName]="currentSample.styleName"
      />

      @if (currentSample.evaluationStatus === 'PendingConsensus') {
        <p role="status">
          There is a scoring discrepancy on this sample.
          <a [routerLink]="['/judge', 'tables', tableId, 'discrepancies']">
            Resolve the discrepancy
          </a>
        </p>
      } @else if (currentSample.evaluationStatus !== 'NotStarted') {
        <p role="status">This sample has already been evaluated.</p>
      } @else {
        <form [formGroup]="form" (ngSubmit)="onSubmit()">
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
                @if (remainingChars(section.key) > 0) {
                  {{ remainingChars(section.key) }} more character{{
                    remainingChars(section.key) === 1 ? '' : 's'
                  }}
                  needed (minimum {{ minCommentLength }}).
                } @else {
                  Minimum length met.
                }
              </p>
            </fieldset>
          }

          <p class="total-display">
            Total (read-only, computed by the server on submit): {{ total() }}
          </p>

          @if (submitError(); as message) {
            <p role="alert">{{ message }}</p>
          }

          <button type="submit" [disabled]="form.invalid || submitting()">Submit evaluation</button>
        </form>
      }
    }
  `,
  styles: `
    .offline-badge {
      background: #fef3c7;
      color: #92400e;
      padding: 0.5rem 0.75rem;
      border-radius: 0.5rem;
      font-weight: 600;
      display: inline-block;
    }

    .sample-style {
      color: #4b5563;
      margin-top: -0.5rem;
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

    .total-display {
      font-weight: 600;
    }
  `,
})
export class EvaluationSheetComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(TastingOrderApiService);
  private readonly syncService = inject(SyncService);

  protected readonly tableId = this.route.snapshot.paramMap.get('tableId')!;
  protected readonly beerEntryId = this.route.snapshot.paramMap.get('beerEntryId')!;

  protected readonly sections = SECTIONS;
  protected readonly minCommentLength = MIN_COMMENT_LENGTH;
  protected readonly form = buildForm();

  protected readonly sample = signal<JudgeSample | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly submitting = signal(false);
  protected readonly submitError = signal<string | null>(null);
  protected readonly isOffline = signal(!navigator.onLine);

  private readonly onlineListener = () => this.isOffline.set(false);
  private readonly offlineListener = () => this.isOffline.set(true);

  constructor() {
    void this.initialize();
  }

  ngOnInit(): void {
    window.addEventListener('online', this.onlineListener);
    window.addEventListener('offline', this.offlineListener);
  }

  ngOnDestroy(): void {
    window.removeEventListener('online', this.onlineListener);
    window.removeEventListener('offline', this.offlineListener);
  }

  protected remainingChars(key: EvaluationSectionConfig['key']): number {
    const section = SECTIONS.find((s) => s.key === key)!;
    const value = (this.form.get(section.commentControl)?.value as string) ?? '';
    return Math.max(0, MIN_COMMENT_LENGTH - value.length);
  }

  protected total(): number {
    return SECTIONS.reduce((sum, section) => {
      const value = this.form.get(section.scoreControl)?.value as number | null;
      return sum + (value ?? 0);
    }, 0);
  }

  protected async onSubmit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.submitError.set(null);

    const { scores, comments } = this.buildPayload();
    // The idempotency key's documented format is {competitionId}:{tableId}:{judgeId}:{entryId},
    // but no judge-facing endpoint currently exposes competitionId/judgeId to the frontend, and
    // the backend only checks the header's *presence*, never its content (the real idempotency
    // guarantee is the server-side (judge, entry) unique constraint) — a judge only ever evaluates
    // a given entry once at a given table, so (tableId, beerEntryId) is already sufficient here.
    const idempotencyKey = `${this.tableId}:${this.beerEntryId}`;

    try {
      await this.syncService.submit(
        idempotencyKey,
        this.tableId,
        this.beerEntryId,
        scores,
        comments,
      );
      // Both a confirmed and a merely-enqueued outcome navigate back: the offline-first guarantee
      // is that the judge's submit action is instant and durable regardless of connectivity — the
      // sample's status will reflect the real state on the next fetch/reconcile of the table, and
      // SyncService itself clears the draft once a later replay actually confirms it server-side.
      await this.router.navigate(['/judge', 'tables', this.tableId]);
    } catch (error) {
      this.submitting.set(false);
      this.submitError.set(this.describeSubmitError(error));
    }
  }

  private buildPayload(): { scores: EvaluationScores; comments: EvaluationComments } {
    const raw = this.form.getRawValue() as Record<string, number | string>;
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

  private describeSubmitError(error: unknown): string {
    if (!(error instanceof ApiError)) {
      // SyncService's submit() only rejects with a non-ApiError when the durable outbox write
      // itself failed (storage unavailable) — the spec edge case that the judge must be warned
      // immediately rather than silently losing offline protection.
      return "This evaluation couldn't be saved locally (your device's storage may be full or restricted). Try again, or use a different device/browser.";
    }

    switch (error.urn) {
      case 'urn:birrapoint:order-not-fixed':
        return 'The tasting order for this table has not been fixed yet.';
      case 'urn:birrapoint:out-of-sequence':
        return 'This sample is not the next one in your tasting order. Go back and refresh the table.';
      case 'urn:birrapoint:table-closed':
        return 'This table has been closed; no further evaluations can be submitted.';
      case 'urn:birrapoint:invalid-state-transition':
        return 'This competition is not currently open for evaluation.';
      default:
        return errorMessage(error);
    }
  }

  private async initialize(): Promise<void> {
    this.loadSample();

    const draft = await this.syncService.loadDraft(this.beerEntryId);
    if (draft) {
      this.form.patchValue(
        {
          aromaScore: draft.scores.aroma,
          aromaComment: draft.comments.aroma,
          appearanceScore: draft.scores.appearance,
          appearanceComment: draft.comments.appearance,
          flavorScore: draft.scores.flavor,
          flavorComment: draft.comments.flavor,
          mouthfeelScore: draft.scores.mouthfeel,
          mouthfeelComment: draft.comments.mouthfeel,
          overallScore: draft.scores.overall,
          overallComment: draft.comments.overall,
        },
        { emitEvent: false },
      );
    }

    // Every field change is durably drafted (FR-026/SC-003) — debounced inside SyncService itself,
    // this subscription just forwards the current values plainly on each change. A rejection
    // (storage unavailable) is swallowed here rather than surfaced per-keystroke; the same failure
    // mode is caught loudly at submit time instead, when it actually blocks the judge's progress.
    this.form.valueChanges.subscribe(() => {
      const { scores, comments } = this.buildPayload();
      this.syncService.saveDraft(this.beerEntryId, this.tableId, scores, comments).catch(() => {
        // Best-effort per keystroke; submit() surfaces a storage failure loudly instead.
      });
    });
  }

  private loadSample(): void {
    this.loadError.set(null);
    this.api.getTableSamples(this.tableId).subscribe({
      next: (samples) => {
        const match = samples.find((s) => s.beerEntryId === this.beerEntryId) ?? null;
        if (!match) {
          this.loadError.set('This sample was not found for this table.');
          return;
        }
        this.sample.set(match);
        cacheSample(match);
      },
      error: (error: unknown) => {
        const apiError = toGenericApiError(error);
        // status 0 = the request never reached the server at all (offline/connectivity failure,
        // not a real domain rejection returned by the API) -- if this exact sample was already
        // fetched successfully earlier in the session, fall back to that snapshot rather than
        // stranding an in-progress (or freshly-restarted, still-offline) sheet behind a load error
        // it can never resolve without connectivity (US7 AC3/quickstart scenario 7: "restart the
        // app [while offline], verify data intact"). Any other failure (a real 404/403 the server
        // actively returned) still blocks the view exactly as before -- there is no legitimate
        // sample to fall back to in that case.
        if (apiError.status === 0) {
          const cached = readCachedSample(this.beerEntryId);
          if (cached) {
            this.sample.set(cached);
            return;
          }
        }
        this.loadError.set(errorMessage(apiError));
      },
    });
  }
}
