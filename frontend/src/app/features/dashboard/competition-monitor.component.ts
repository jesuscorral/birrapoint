import type { OnDestroy, OnInit } from '@angular/core';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HubConnectionState } from '@microsoft/signalr';
import type { Subscription } from 'rxjs';
import { forkJoin } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { CompetitionsApiService } from '../../core/api/competitions-api.service';
import type { CompetitionDetail } from '../../core/api/competitions-api.service';
import { EntriesApiService } from '../../core/api/entries-api.service';
import type { EntryListItem } from '../../core/api/entries-api.service';
import { MonitoringApiService } from '../../core/api/monitoring-api.service';
import type {
  EntryEvaluationsResult,
  TableProgressSummary,
} from '../../core/api/monitoring-api.service';
import { CompetitionHubService } from '../../core/realtime/competition-hub.service';
import type {
  EvaluationCompletedEvent,
  TableClosedEvent,
  TableOrderFixedEvent,
} from '../../core/realtime/competition-hub.events';

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

function errorMessage(error: ApiError): string {
  return error.detail ?? error.title;
}

// T070/US9: live organizer monitoring dashboard. Table progress and closed state are updated
// in place from EvaluationCompleted/TableClosed hub notifications (FR-037's "no reload, no
// flicker" requirement) rather than a full refetch — the initial GETs remain the source of
// truth, the hub events are just notifications layered on top per contracts/signalr-hub.md.
//
// The sample drill-down (FR-038 Acceptance Scenario 2) is read-only: every judge's five scores,
// five comments, total, and status, plus the consolidated mean once the table has closed. It
// always fetches fresh via getEntryEvaluations on selection rather than caching TableClosed's
// consolidatedScores payload — simpler, and correctness only depends on one round trip per click.
@Component({
  selector: 'app-competition-monitor',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p><a routerLink="/organizer/dashboard">&larr; Competitions</a></p>

    @if (loadError(); as message) {
      <p role="alert">{{ message }}</p>
    }

    @if (!loadError()) {
      @if (competition(); as comp) {
        <h1>{{ comp.name }}</h1>
        <p class="competition-meta">{{ comp.venue }} &middot; {{ comp.state }}</p>
      }

      <ul class="table-progress-list">
        @for (row of tableRows(); track row.tableId) {
          <li class="table-progress-row" [attr.data-table-id]="row.tableId">
            <div class="table-progress-header">
              <span class="table-name">{{ row.name }}</span>
              <span [class]="tableBadgeClass(row.state)">{{ row.state }}</span>
              <span class="table-progress-count"
                >{{ row.completed }} / {{ row.expected }} ({{ row.percent }}%)</span
              >
            </div>

            @if (orderFixedNote(row.tableId); as note) {
              <p role="status" class="order-fixed-note">{{ note }}</p>
            }

            <ul class="sample-list" [attr.aria-label]="'Samples at ' + row.name">
              @for (entry of samplesByTable().get(row.tableId) ?? []; track entry.id) {
                <li>
                  <button
                    type="button"
                    [attr.data-entry-id]="entry.id"
                    (click)="onSelectSample(entry)"
                  >
                    {{ entry.blindCode }}
                  </button>
                </li>
              }
            </ul>
          </li>
        }
      </ul>
    }

    @if (selectedEntry(); as entry) {
      <section [attr.aria-label]="'Evaluations for ' + entry.blindCode" class="drill-down">
        <h2>{{ entry.blindCode }}</h2>
        <button type="button" (click)="onCloseDrillDown()">Close</button>

        @if (entryEvaluationsLoading()) {
          <p>Loading…</p>
        }
        @if (entryEvaluationsError(); as message) {
          <p role="alert">{{ message }}</p>
        }
        @if (entryEvaluations(); as result) {
          <p class="consolidated-mean">
            Consolidated mean:
            {{ result.consolidatedMean !== null ? result.consolidatedMean : 'not yet closed' }}
          </p>

          @for (evaluation of result.evaluations; track $index) {
            <article class="evaluation-audit">
              <h3>{{ evaluation.judgeDisplayName }}</h3>
              <p class="evaluation-status">Status: {{ evaluation.status }}</p>
              <dl>
                <dt>Aroma</dt>
                <dd>{{ evaluation.scores.aroma }} — {{ evaluation.comments.aroma }}</dd>
                <dt>Appearance</dt>
                <dd>{{ evaluation.scores.appearance }} — {{ evaluation.comments.appearance }}</dd>
                <dt>Flavor</dt>
                <dd>{{ evaluation.scores.flavor }} — {{ evaluation.comments.flavor }}</dd>
                <dt>Mouthfeel</dt>
                <dd>{{ evaluation.scores.mouthfeel }} — {{ evaluation.comments.mouthfeel }}</dd>
                <dt>Overall</dt>
                <dd>{{ evaluation.scores.overall }} — {{ evaluation.comments.overall }}</dd>
              </dl>
              <p class="evaluation-total">Total: {{ evaluation.total }}</p>
            </article>
          }
        }
      </section>
    }
  `,
  styles: `
    .competition-meta {
      color: #4b5563;
    }

    .table-progress-list {
      list-style: none;
      margin: 1rem 0 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }

    .table-progress-row {
      border: 1px solid #d1d5db;
      border-radius: 0.5rem;
      padding: 0.75rem 1rem;
    }

    .table-progress-header {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }

    .table-name {
      font-weight: 600;
    }

    .badge {
      padding: 0.15rem 0.5rem;
      border-radius: 9999px;
      font-size: 0.8rem;
    }

    .badge--open {
      background: #dcfce7;
      color: #166534;
    }

    .badge--closed {
      background: #e5e7eb;
      color: #374151;
    }

    .table-progress-count {
      margin-left: auto;
      color: #4b5563;
    }

    .order-fixed-note {
      color: #92400e;
      font-size: 0.9rem;
    }

    .sample-list {
      list-style: none;
      margin: 0.5rem 0 0;
      padding: 0;
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }

    .drill-down {
      margin-top: 1.5rem;
      border: 1px solid #d1d5db;
      border-radius: 0.5rem;
      padding: 1rem;
    }

    .evaluation-audit {
      border-top: 1px solid #e5e7eb;
      padding-top: 0.75rem;
      margin-top: 0.75rem;
    }
  `,
})
export class CompetitionMonitorComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly competitionsApi = inject(CompetitionsApiService);
  private readonly monitoringApi = inject(MonitoringApiService);
  private readonly entriesApi = inject(EntriesApiService);
  private readonly hub = inject(CompetitionHubService);

  private readonly competitionId = this.route.snapshot.paramMap.get('id')!;

  private evaluationCompletedSubscription: Subscription | null = null;
  private tableClosedSubscription: Subscription | null = null;
  private tableOrderFixedSubscription: Subscription | null = null;

  protected readonly competition = signal<CompetitionDetail | null>(null);
  protected readonly tableRows = signal<TableProgressSummary[]>([]);
  protected readonly entries = signal<EntryListItem[]>([]);
  protected readonly loadError = signal<string | null>(null);
  protected readonly orderFixedNotes = signal<Map<string, string>>(new Map());

  protected readonly selectedEntry = signal<EntryListItem | null>(null);
  protected readonly entryEvaluations = signal<EntryEvaluationsResult | null>(null);
  protected readonly entryEvaluationsLoading = signal(false);
  protected readonly entryEvaluationsError = signal<string | null>(null);

  protected readonly samplesByTable = computed(() => {
    const map = new Map<string, EntryListItem[]>();
    for (const entry of this.entries()) {
      if (!entry.tastingTableId) {
        continue;
      }
      const list = map.get(entry.tastingTableId) ?? [];
      list.push(entry);
      map.set(entry.tastingTableId, list);
    }
    return map;
  });

  private hasConnectedBefore = false;

  constructor() {
    this.loadAll();

    // Events are notifications, not the source of truth (contracts/signalr-hub.md) — anything
    // missed while disconnected (a network blip) must be reconciled by re-fetching once the hub
    // reconnects, not left stale until a manual reload. Only re-fetches on a *re*-connect, not the
    // initial one (which loadAll() above already covers) — senior-code-reviewer finding on PR #24.
    effect(() => {
      if (this.hub.state() === HubConnectionState.Connected) {
        if (this.hasConnectedBefore) {
          this.loadAll();
        }
        this.hasConnectedBefore = true;
      }
    });
  }

  ngOnInit(): void {
    void this.connectToHub();
  }

  ngOnDestroy(): void {
    this.evaluationCompletedSubscription?.unsubscribe();
    this.tableClosedSubscription?.unsubscribe();
    this.tableOrderFixedSubscription?.unsubscribe();
    void this.hub.leaveCompetition(this.competitionId).catch(() => {
      // Best-effort: leaving on navigation-away is a courtesy, not a functional requirement.
    });
  }

  protected tableBadgeClass(state: TableProgressSummary['state']): string {
    return `badge badge--${state.toLowerCase()}`;
  }

  protected orderFixedNote(tableId: string): string | null {
    return this.orderFixedNotes().get(tableId) ?? null;
  }

  protected onSelectSample(entry: EntryListItem): void {
    this.selectedEntry.set(entry);
    this.entryEvaluations.set(null);
    this.entryEvaluationsError.set(null);
    this.entryEvaluationsLoading.set(true);

    this.monitoringApi.getEntryEvaluations(this.competitionId, entry.id).subscribe({
      next: (result) => {
        this.entryEvaluationsLoading.set(false);
        this.entryEvaluations.set(result);
      },
      error: (error: unknown) => {
        this.entryEvaluationsLoading.set(false);
        this.entryEvaluationsError.set(errorMessage(toGenericApiError(error)));
      },
    });
  }

  protected onCloseDrillDown(): void {
    this.selectedEntry.set(null);
    this.entryEvaluations.set(null);
    this.entryEvaluationsError.set(null);
  }

  private loadAll(): void {
    this.loadError.set(null);
    forkJoin({
      competition: this.competitionsApi.getById(this.competitionId),
      progress: this.monitoringApi.getProgress(this.competitionId),
      entries: this.entriesApi.getEntries(this.competitionId),
    }).subscribe({
      next: ({ competition, progress, entries }) => {
        this.competition.set(competition);
        this.tableRows.set(progress);
        this.entries.set(entries);
      },
      error: (error: unknown) => this.loadError.set(errorMessage(toGenericApiError(error))),
    });
  }

  private async connectToHub(): Promise<void> {
    try {
      await this.hub.start();
      await this.hub.joinCompetitionAsOrganizer(this.competitionId);
      this.evaluationCompletedSubscription = this.hub
        .on('EvaluationCompleted')
        .subscribe((event) => this.applyEvaluationCompleted(event));
      this.tableClosedSubscription = this.hub
        .on('TableClosed')
        .subscribe((event) => this.applyTableClosed(event));
      this.tableOrderFixedSubscription = this.hub
        .on('TableOrderFixed')
        .subscribe((event) => this.applyTableOrderFixed(event));
    } catch {
      // Realtime is a best-effort notification channel (contracts/signalr-hub.md): the dashboard
      // stays fully functional over REST without it — an organizer just won't see live progress
      // and will need to reload to pick up changes made elsewhere.
    }
  }

  private applyEvaluationCompleted(event: EvaluationCompletedEvent): void {
    this.tableRows.update((rows) =>
      rows.map((row) => (row.tableId === event.tableId ? { ...row, ...event.tableProgress } : row)),
    );
  }

  private applyTableClosed(event: TableClosedEvent): void {
    this.tableRows.update((rows) =>
      rows.map((row) => (row.tableId === event.tableId ? { ...row, state: 'Closed' } : row)),
    );
  }

  private applyTableOrderFixed(event: TableOrderFixedEvent): void {
    this.orderFixedNotes.update((notes) => {
      const next = new Map(notes);
      next.set(event.tableId, `Order fixed by ${event.fixedByDisplayName}.`);
      return next;
    });
  }
}
