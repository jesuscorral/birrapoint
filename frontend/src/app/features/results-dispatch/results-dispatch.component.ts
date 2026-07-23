import type { OnDestroy, OnInit } from '@angular/core';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import type { Subscription } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { DispatchApiService } from '../../core/api/dispatch-api.service';
import type { DispatchStatusRow } from '../../core/api/dispatch-api.service';
import { CompetitionHubService } from '../../core/realtime/competition-hub.service';
import type { DispatchProgressEvent } from '../../core/realtime/competition-hub.events';

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

function errorMessage(error: ApiError): string {
  return error.detail ?? error.title;
}

const JOB_TYPE_LABEL: Record<string, string> = {
  GeneratePdfs: 'Generating PDFs',
  BundleZip: 'Bundling ZIP',
  SendResultEmail: 'Sending emails',
};

// T077/US10: post-Finalized results & dispatch screen. There is no cheap standalone "is the ZIP
// ready" check in the contract (contracts/rest-api.md GET .../results/archive is the readiness
// check *and* the download in one call, returning the full blob body either way) — probing it on
// load would mean eagerly fetching a potentially large archive just to find out. Readiness is
// therefore driven by two signals instead: the live DispatchProgress BundleZip/Completed event
// (exact, but only observed while this page is open during the pipeline), and — for a page loaded
// after the pipeline already finished, with no live event to catch — a fallback proxy: every
// participant's email reaching a terminal status (Completed/Failed) implies BundleZip already ran,
// since the pipeline is strictly GeneratePdfs -> BundleZip -> SendResultEmail (T075). This proxy
// is conservative (the ZIP may in fact be ready earlier, mid-email-sending) but never wrong in the
// unsafe direction. The archive endpoint itself is only ever called on an explicit user click.
@Component({
  selector: 'app-results-dispatch',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p>
      <a [routerLink]="['/organizer', 'competitions', competitionId, 'monitor']">&larr; Monitor</a>
    </p>

    <h1>Results &amp; Dispatch</h1>

    @if (loadError(); as message) {
      <p role="alert">{{ message }}</p>
    }
    @if (retryError(); as message) {
      <p role="alert">{{ message }}</p>
    }

    @if (pipelineStageLabel(); as label) {
      <p role="status" class="pipeline-stage">{{ label }}</p>
    }

    <div class="archive-actions">
      <button type="button" [disabled]="!archiveReady() || downloading()" (click)="onDownload()">
        Download results ZIP
      </button>
      <button type="button" (click)="onRefresh()">Refresh status</button>
      @if (archiveNotReadyMessage(); as message) {
        <p role="status">{{ message }}</p>
      }
    </div>

    @if (!loadError()) {
      @if (failedParticipantIds().length > 1) {
        <button type="button" [disabled]="retryingAll()" (click)="onRetryAllFailed()">
          Retry all failed
        </button>
      }

      <table class="dispatch-table">
        <thead>
          <tr>
            <th scope="col">Email</th>
            <th scope="col">Status</th>
            <th scope="col">Attempts</th>
            <th scope="col">Last error</th>
            <th scope="col">Action</th>
          </tr>
        </thead>
        <tbody>
          @for (row of rows(); track row.participantId) {
            <tr [attr.data-participant-id]="row.participantId">
              <td>{{ row.email }}</td>
              <td>
                <span [class]="statusBadgeClass(row.status)">{{ row.status }}</span>
              </td>
              <td>{{ row.attempts }}</td>
              <td>{{ row.lastError ?? '—' }}</td>
              <td>
                @if (row.status === 'Failed') {
                  <button
                    type="button"
                    [disabled]="isRetrying(row.participantId)"
                    (click)="onRetry(row)"
                  >
                    Retry
                  </button>
                }
              </td>
            </tr>
          }
        </tbody>
      </table>
    }
  `,
  styles: `
    .pipeline-stage {
      color: #1e40af;
    }

    .archive-actions {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      margin: 1rem 0;
    }

    .dispatch-table {
      width: 100%;
      border-collapse: collapse;
      margin-top: 1rem;
    }

    .dispatch-table th,
    .dispatch-table td {
      text-align: left;
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid #e5e7eb;
    }

    .badge {
      padding: 0.15rem 0.5rem;
      border-radius: 9999px;
      font-size: 0.8rem;
    }

    .badge--completed {
      background: #dcfce7;
      color: #166534;
    }

    .badge--failed {
      background: #fee2e2;
      color: #991b1b;
    }

    .badge--pending,
    .badge--running {
      background: #fef3c7;
      color: #92400e;
    }
  `,
})
export class ResultsDispatchComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly dispatchApi = inject(DispatchApiService);
  private readonly hub = inject(CompetitionHubService);

  protected readonly competitionId = this.route.snapshot.paramMap.get('id')!;

  private dispatchProgressSubscription: Subscription | null = null;

  protected readonly rows = signal<DispatchStatusRow[]>([]);
  protected readonly loadError = signal<string | null>(null);

  protected readonly retryError = signal<string | null>(null);
  protected readonly retryingParticipantIds = signal<Set<string>>(new Set());
  protected readonly retryingAll = signal(false);

  protected readonly pipelineEvent = signal<DispatchProgressEvent | null>(null);
  protected readonly archiveReadyFromHub = signal(false);
  protected readonly downloading = signal(false);
  protected readonly archiveNotReadyMessage = signal<string | null>(null);

  protected readonly failedParticipantIds = computed(() =>
    this.rows()
      .filter((row) => row.status === 'Failed')
      .map((row) => row.participantId),
  );

  protected readonly archiveReady = computed(() => {
    if (this.archiveReadyFromHub()) {
      return true;
    }
    const rows = this.rows();
    return (
      rows.length > 0 && rows.every((row) => row.status === 'Completed' || row.status === 'Failed')
    );
  });

  protected readonly pipelineStageLabel = computed(() => {
    const event = this.pipelineEvent();
    if (!event) {
      return null;
    }
    const label = JOB_TYPE_LABEL[event.jobType] ?? event.jobType;
    return `${label}: ${event.status}${event.detail ? ` — ${event.detail}` : ''}`;
  });

  constructor() {
    this.loadAll();
  }

  ngOnInit(): void {
    void this.connectToHub();
  }

  ngOnDestroy(): void {
    this.dispatchProgressSubscription?.unsubscribe();
    void this.hub.leaveCompetition(this.competitionId).catch(() => {
      // Best-effort: leaving on navigation-away is a courtesy, not a functional requirement.
    });
  }

  protected statusBadgeClass(status: string): string {
    return `badge badge--${status.toLowerCase()}`;
  }

  protected isRetrying(participantId: string): boolean {
    return this.retryingParticipantIds().has(participantId);
  }

  protected onRefresh(): void {
    this.loadAll();
  }

  protected onRetry(row: DispatchStatusRow): void {
    if (this.isRetrying(row.participantId)) {
      return;
    }
    this.retryError.set(null);
    this.retryingParticipantIds.update((ids) => new Set(ids).add(row.participantId));

    this.dispatchApi.retryDispatch(this.competitionId, [row.participantId]).subscribe({
      next: () => {
        this.clearRetrying(row.participantId);
        this.loadAll();
      },
      error: (error: unknown) => {
        this.clearRetrying(row.participantId);
        this.retryError.set(errorMessage(toGenericApiError(error)));
      },
    });
  }

  protected onRetryAllFailed(): void {
    const participantIds = this.failedParticipantIds();
    if (participantIds.length === 0 || this.retryingAll()) {
      return;
    }
    this.retryError.set(null);
    this.retryingAll.set(true);

    this.dispatchApi.retryDispatch(this.competitionId, participantIds).subscribe({
      next: () => {
        this.retryingAll.set(false);
        this.loadAll();
      },
      error: (error: unknown) => {
        this.retryingAll.set(false);
        this.retryError.set(errorMessage(toGenericApiError(error)));
      },
    });
  }

  protected onDownload(): void {
    if (this.downloading()) {
      return;
    }
    this.downloading.set(true);
    this.archiveNotReadyMessage.set(null);

    this.dispatchApi.downloadResultsArchive(this.competitionId).subscribe({
      next: (result) => {
        this.downloading.set(false);
        if (result.ready) {
          this.triggerBlobDownload(result.blob);
        } else {
          this.archiveNotReadyMessage.set(
            `Results archive is still being generated (${result.status}). Try again shortly.`,
          );
        }
      },
      error: (error: unknown) => {
        this.downloading.set(false);
        this.archiveNotReadyMessage.set(errorMessage(toGenericApiError(error)));
      },
    });
  }

  // Appends the anchor to the DOM (some engines, notably Firefox, don't reliably fire a click on
  // a detached element) and defers the revoke to a macrotask instead of doing it synchronously
  // right after click() (some engines, including iOS Safari — this PWA's stated offline-engine
  // constraint, R-08 — can cancel a still-in-flight download if the object URL is revoked too
  // eagerly). Chromium (the only E2E browser) tolerates both shortcuts, which is how this shipped
  // originally (senior-code-reviewer finding on PR #25).
  private triggerBlobDownload(blob: Blob): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `results-${this.competitionId}.zip`;
    anchor.style.display = 'none';
    document.body.appendChild(anchor);
    anchor.click();
    setTimeout(() => {
      document.body.removeChild(anchor);
      URL.revokeObjectURL(url);
    }, 0);
  }

  private clearRetrying(participantId: string): void {
    this.retryingParticipantIds.update((ids) => {
      const next = new Set(ids);
      next.delete(participantId);
      return next;
    });
  }

  private loadAll(): void {
    this.loadError.set(null);
    this.dispatchApi.getDispatchStatus(this.competitionId).subscribe({
      next: (rows) => this.rows.set(rows),
      error: (error: unknown) => this.loadError.set(errorMessage(toGenericApiError(error))),
    });
  }

  private async connectToHub(): Promise<void> {
    try {
      await this.hub.start();
      await this.hub.joinCompetitionAsOrganizer(this.competitionId);
      this.dispatchProgressSubscription = this.hub
        .on('DispatchProgress')
        .subscribe((event) => this.applyDispatchProgress(event));
    } catch {
      // Realtime is a best-effort notification channel (contracts/signalr-hub.md): the screen
      // stays fully functional over REST without it via the manual "Refresh status" action.
    }
  }

  private applyDispatchProgress(event: DispatchProgressEvent): void {
    this.pipelineEvent.set(event);
    if (event.jobType === 'BundleZip' && event.status === 'Completed') {
      this.archiveReadyFromHub.set(true);
    }
  }
}
