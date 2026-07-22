import { CdkTrapFocus } from '@angular/cdk/a11y';
import type { CdkDragDrop } from '@angular/cdk/drag-drop';
import { CdkDrag, CdkDragHandle, CdkDropList, moveItemInArray } from '@angular/cdk/drag-drop';
import type { OnDestroy, OnInit } from '@angular/core';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import type { Subscription } from 'rxjs';
import { filter, forkJoin } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { CompetitionHubService } from '../../core/realtime/competition-hub.service';
import type {
  TableClosedEvent,
  TableOrderFixedEvent,
} from '../../core/realtime/competition-hub.events';
import { TastingOrderApiService } from './tasting-order-api.service';
import type { JudgeSample, JudgeTableSummary } from './tasting-order-api.service';

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

function errorMessage(error: ApiError): string {
  return error.detail ?? error.title;
}

function swap<T>(items: T[], a: number, b: number): T[] {
  const next = [...items];
  [next[a], next[b]] = [next[b], next[a]];
  return next;
}

// T053/US6: blind per-table sample/tasting-order view. Only ever renders blindCode/styleCode/
// styleName from JudgeSample — the BR-01/FR-019 anonymity boundary — never any entrant field.
//
// Reordering: while the order isn't fixed, samples can be reordered via CDK drag-and-drop
// (`cdkDragHandle`-restricted, not the whole row) or the Move up/down buttons — FR-020's
// keyboard-accessible equivalent, built alongside the drag gesture rather than after it.
// Unlike T048B's judge/beer tokens (where the whole draggable element IS the only interactive
// target, so a pointer-movement threshold cleanly disambiguates a click-to-open from a
// drag-start), each row here already has two independent interactive buttons nested inside it —
// `cdkDragHandle` restricts the drag surface to a dedicated handle so button clicks are never in
// contention with drag detection in the first place, rather than resolved heuristically.
//
// Fixing: a one-shot, irreversible action gated by an explicit confirm step. Once fixed (locally,
// or via a live `TableOrderFixed` hub notification for another judge fixing it while this table is
// open), the list becomes read-only and shows who fixed it.
@Component({
  selector: 'app-judge-table-order',
  imports: [RouterLink, CdkTrapFocus, CdkDropList, CdkDrag, CdkDragHandle],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p><a routerLink="/judge/tables">&larr; My tables</a></p>
    <h1>{{ tableName() }}</h1>

    @if (loadError(); as message) {
      <p role="alert">{{ message }}</p>
    }

    @if (!loadError()) {
      @if (tableClosed()) {
        <p role="status" class="order-status order-status--closed">
          Table closed. Scores are now permanently locked.
        </p>
      } @else if (orderFixed()) {
        <p role="status" class="order-status order-status--fixed">
          Order fixed{{ fixedByDisplayName() ? ' by ' + fixedByDisplayName() : '' }}.
        </p>
      } @else if (samples().length > 0) {
        <p class="order-status">
          Drag to reorder, or use the Move up/down buttons. Fixing the order is permanent.
        </p>
      }

      @if (fixError(); as message) {
        <p role="alert">{{ message }}</p>
      }

      @if (closeError(); as message) {
        <p role="alert">{{ message }}</p>
      }

      @if (samples().length === 0) {
        <p>No samples assigned to this table yet.</p>
      } @else {
        <ol
          class="sample-order-list"
          aria-label="Tasting order"
          cdkDropList
          [cdkDropListDisabled]="orderFixed()"
          (cdkDropListDropped)="onDrop($event)"
        >
          @for (sample of samples(); track sample.beerEntryId; let i = $index) {
            <li class="sample-row" cdkDrag [cdkDragDisabled]="orderFixed()">
              @if (!orderFixed()) {
                <span class="drag-handle" cdkDragHandle aria-hidden="true">&#10021;</span>
              }
              <span class="sample-position">{{ i + 1 }}</span>
              <span class="sample-blind-code">{{ sample.blindCode }}</span>
              <span class="sample-style">{{ sample.styleName }} ({{ sample.styleCode }})</span>

              @if (orderFixed()) {
                <span class="sample-evaluation-status">
                  @switch (sample.evaluationStatus) {
                    @case ('Submitted') {
                      <span class="badge badge--done">Submitted</span>
                    }
                    @case ('PendingConsensus') {
                      <span class="badge badge--pending-consensus">Pending consensus</span>
                    }
                    @default {
                      @if (sample.beerEntryId === firstReachableEntryId()) {
                        <a
                          [routerLink]="[
                            '/judge',
                            'tables',
                            tableId,
                            'samples',
                            sample.beerEntryId,
                          ]"
                          class="evaluate-action"
                        >
                          Evaluate
                        </a>
                      } @else {
                        <span class="badge badge--locked">Locked</span>
                      }
                    }
                  }
                </span>
              }

              @if (!orderFixed()) {
                <span class="sample-move-controls">
                  <button
                    type="button"
                    [attr.aria-label]="'Move ' + sample.blindCode + ' up'"
                    [disabled]="!canMoveUp(i)"
                    (click)="onMoveUp(i)"
                  >
                    &uarr;
                  </button>
                  <button
                    type="button"
                    [attr.aria-label]="'Move ' + sample.blindCode + ' down'"
                    [disabled]="!canMoveDown(i)"
                    (click)="onMoveDown(i)"
                  >
                    &darr;
                  </button>
                </span>
              }
            </li>
          }
        </ol>

        @if (!orderFixed()) {
          <button type="button" [disabled]="fixing()" (click)="onRequestFix()">Fix order</button>
        } @else if (canCloseTable()) {
          <button type="button" [disabled]="closing()" (click)="onRequestClose()">
            Close table
          </button>
        }
      }
    }

    @if (confirmingFix()) {
      <div class="modal-backdrop" role="presentation" (click)="onCancelFixConfirm()">
        <div
          role="alertdialog"
          aria-modal="true"
          aria-label="Confirm fix order"
          class="modal-panel"
          cdkTrapFocus
          cdkTrapFocusAutoCapture
          (click)="$event.stopPropagation()"
          (keydown.escape)="onCancelFixConfirm()"
        >
          <h2>Fix tasting order</h2>
          <p>
            This locks the tasting order for everyone at this table and cannot be undone. Continue?
          </p>
          <button type="button" [disabled]="fixing()" (click)="onConfirmFix()">
            Confirm fix order
          </button>
          <button type="button" (click)="onCancelFixConfirm()">Cancel</button>
        </div>
      </div>
    }

    @if (confirmingClose()) {
      <div class="modal-backdrop" role="presentation" (click)="onCancelCloseConfirm()">
        <div
          role="alertdialog"
          aria-modal="true"
          aria-label="Confirm close table"
          class="modal-panel"
          cdkTrapFocus
          cdkTrapFocusAutoCapture
          (click)="$event.stopPropagation()"
          (keydown.escape)="onCancelCloseConfirm()"
        >
          <h2>Close table</h2>
          <p>
            This permanently locks the table and its scores for everyone here and cannot be undone.
            Continue?
          </p>
          <button type="button" [disabled]="closing()" (click)="onConfirmClose()">
            Confirm close table
          </button>
          <button type="button" (click)="onCancelCloseConfirm()">Cancel</button>
        </div>
      </div>
    }
  `,
  styles: `
    .order-status {
      margin: 0.5rem 0;
    }

    .order-status--fixed {
      color: #166534;
      font-weight: 600;
    }

    .order-status--closed {
      color: #1e3a8a;
      font-weight: 600;
    }

    .sample-order-list {
      list-style: none;
      margin: 1rem 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }

    .sample-row {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      padding: 0.5rem 0.75rem;
      border: 1px solid #d1d5db;
      border-radius: 0.5rem;
      background: #fff;
    }

    .drag-handle {
      cursor: grab;
      color: #6b7280;
    }

    .sample-position {
      font-weight: 700;
      min-width: 1.5rem;
    }

    .sample-move-controls {
      margin-left: auto;
      display: flex;
      gap: 0.25rem;
    }

    .sample-evaluation-status {
      margin-left: auto;
    }

    .evaluate-action {
      padding: 0.25rem 0.75rem;
      border-radius: 0.375rem;
      background: #2563eb;
      color: #fff;
      text-decoration: none;
      font-weight: 600;
    }

    .badge--done {
      background: #dcfce7;
      color: #166534;
      padding: 0.15rem 0.5rem;
      border-radius: 9999px;
      font-size: 0.8rem;
    }

    .badge--pending-consensus {
      background: #fee2e2;
      color: #991b1b;
      padding: 0.15rem 0.5rem;
      border-radius: 9999px;
      font-size: 0.8rem;
    }

    .badge--locked {
      background: #f3f4f6;
      color: #6b7280;
      padding: 0.15rem 0.5rem;
      border-radius: 9999px;
      font-size: 0.8rem;
    }

    .modal-backdrop {
      position: fixed;
      inset: 0;
      background: rgb(0 0 0 / 50%);
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .modal-panel {
      background: #fff;
      border-radius: 0.5rem;
      padding: 1.5rem;
      min-width: 20rem;
    }
  `,
})
export class JudgeTableOrderComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(TastingOrderApiService);
  private readonly hub = inject(CompetitionHubService);

  protected readonly tableId = this.route.snapshot.paramMap.get('tableId')!;
  private hubSubscription: Subscription | null = null;
  private tableClosedSubscription: Subscription | null = null;

  protected readonly tableSummary = signal<JudgeTableSummary | null>(null);
  protected readonly samples = signal<JudgeSample[]>([]);
  protected readonly orderFixed = signal(false);
  protected readonly fixedByDisplayName = signal<string | null>(null);
  protected readonly loadError = signal<string | null>(null);

  protected readonly confirmingFix = signal(false);
  protected readonly fixing = signal(false);
  protected readonly fixError = signal<string | null>(null);

  // Seeded from the initial GET /me/tables load and flipped on this judge's own successful close,
  // a 409 table-closed race (someone else closed it first), or a live TableClosed hub event — see
  // handleCloseError()/applyTableClosedEvent() below. Not purely derived from `samples()` because
  // it must also reflect a table that was *already* closed before this judge ever opened it.
  protected readonly tableClosed = signal(false);
  protected readonly confirmingClose = signal(false);
  protected readonly closing = signal(false);
  protected readonly closeError = signal<string | null>(null);

  protected readonly tableName = computed(() => this.tableSummary()?.name ?? 'Table');

  // FR-033 close precondition, mirrored client-side purely to gate the button's visibility — the
  // backend re-validates all of this authoritatively (409 evaluations-incomplete/discrepancy-open)
  // on the actual close call.
  protected readonly canCloseTable = computed(
    () =>
      this.orderFixed() &&
      !this.tableClosed() &&
      this.samples().length > 0 &&
      this.samples().every((sample) => sample.evaluationStatus !== 'NotStarted'),
  );

  // FR-022 strict sequencing, once the order is fixed: the only sample a judge may *start*
  // evaluating right now is the first one (in the fixed order) still NotStarted. `samples()`
  // already reflects that order directly (both before and after fixing — the drag/move handlers
  // above mutate it in place, and the fix/hub-event paths replace it with the server's own
  // ordering) — no separate re-sort by sequenceOrder is needed here.
  protected readonly firstReachableEntryId = computed(
    () =>
      this.samples().find((sample) => sample.evaluationStatus === 'NotStarted')?.beerEntryId ??
      null,
  );

  constructor() {
    this.loadAll();
  }

  ngOnInit(): void {
    void this.connectToHub();
  }

  ngOnDestroy(): void {
    this.hubSubscription?.unsubscribe();
    this.tableClosedSubscription?.unsubscribe();
    void this.hub.leaveTable(this.tableId).catch(() => {
      // Best-effort: leaving on navigation-away is a courtesy, not a functional requirement.
    });
  }

  protected onDrop(event: CdkDragDrop<unknown>): void {
    if (this.orderFixed() || event.previousIndex === event.currentIndex) {
      return;
    }
    this.samples.update((current) => {
      const next = [...current];
      moveItemInArray(next, event.previousIndex, event.currentIndex);
      return next;
    });
  }

  protected canMoveUp(index: number): boolean {
    return !this.orderFixed() && index > 0;
  }

  protected canMoveDown(index: number): boolean {
    return !this.orderFixed() && index < this.samples().length - 1;
  }

  protected onMoveUp(index: number): void {
    if (!this.canMoveUp(index)) {
      return;
    }
    this.samples.update((current) => swap(current, index, index - 1));
  }

  protected onMoveDown(index: number): void {
    if (!this.canMoveDown(index)) {
      return;
    }
    this.samples.update((current) => swap(current, index, index + 1));
  }

  protected onRequestFix(): void {
    this.fixError.set(null);
    this.confirmingFix.set(true);
  }

  protected onCancelFixConfirm(): void {
    this.confirmingFix.set(false);
  }

  protected onConfirmFix(): void {
    if (this.fixing()) {
      return;
    }
    this.fixing.set(true);
    this.fixError.set(null);

    const orderedBeerEntryIds = this.samples().map((sample) => sample.beerEntryId);
    this.api.fixOrder(this.tableId, orderedBeerEntryIds).subscribe({
      next: (samples) => {
        this.fixing.set(false);
        this.confirmingFix.set(false);
        this.samples.set(samples);
        this.orderFixed.set(true);
        this.refreshFixedBy();
      },
      error: (error: unknown) => {
        this.fixing.set(false);
        this.confirmingFix.set(false);
        this.handleFixError(error);
      },
    });
  }

  protected onRequestClose(): void {
    this.closeError.set(null);
    this.confirmingClose.set(true);
  }

  protected onCancelCloseConfirm(): void {
    this.confirmingClose.set(false);
  }

  protected onConfirmClose(): void {
    if (this.closing()) {
      return;
    }
    this.closing.set(true);
    this.closeError.set(null);

    this.api.closeTable(this.tableId).subscribe({
      next: () => {
        this.closing.set(false);
        this.confirmingClose.set(false);
        this.tableClosed.set(true);
      },
      error: (error: unknown) => {
        this.closing.set(false);
        this.confirmingClose.set(false);
        this.handleCloseError(error);
      },
    });
  }

  private async connectToHub(): Promise<void> {
    try {
      await this.hub.start();
      await this.hub.joinTable(this.tableId);
      this.hubSubscription = this.hub
        .on('TableOrderFixed')
        .pipe(filter((event) => event.tableId === this.tableId))
        .subscribe((event) => this.applyOrderFixedEvent(event));
      this.tableClosedSubscription = this.hub
        .on('TableClosed')
        .pipe(filter((event: TableClosedEvent) => event.tableId === this.tableId))
        .subscribe(() => this.tableClosed.set(true));
    } catch {
      // Realtime is a best-effort notification channel (contracts/signalr-hub.md): the samples
      // view stays fully functional over REST without it — a judge just won't see another
      // judge's fix (or close) live and will pick it up on next load instead.
    }
  }

  private loadAll(): void {
    this.loadError.set(null);
    forkJoin({
      tables: this.api.getMyTables(),
      samples: this.api.getTableSamples(this.tableId),
    }).subscribe({
      next: ({ tables, samples }) => {
        const summary = tables.find((table) => table.tableId === this.tableId) ?? null;
        this.tableSummary.set(summary);
        this.samples.set(samples);
        this.orderFixed.set(summary?.orderFixed ?? false);
        this.fixedByDisplayName.set(summary?.orderFixedBy ?? null);
        this.tableClosed.set(summary?.tableState === 'Closed');
      },
      error: (error: unknown) => this.loadError.set(errorMessage(toGenericApiError(error))),
    });
  }

  // Refetches, rather than assumes, who fixed the order — mirrors table-management's "refetch
  // for full correctness" convention: the live TableOrderFixed hub event already carries
  // fixedByDisplayName for free when the connection is up, but this call keeps the label correct
  // even if the realtime channel is degraded or unavailable at the moment of this judge's own fix.
  private refreshFixedBy(): void {
    this.api.getMyTables().subscribe({
      next: (tables) => {
        const summary = tables.find((table) => table.tableId === this.tableId) ?? null;
        this.tableSummary.set(summary);
        this.fixedByDisplayName.set(summary?.orderFixedBy ?? null);
      },
      error: () => {
        // Best-effort: the order is already locked (authoritative from the fixOrder response
        // above) — a failure here only leaves the "fixed by" label blank, not a functional issue.
      },
    });
  }

  private applyOrderFixedEvent(event: TableOrderFixedEvent): void {
    const sequenceByEntryId = new Map(
      event.orderedSamples.map((sample) => [sample.beerEntryId, sample.sequenceOrder]),
    );
    this.samples.update((current) =>
      [...current]
        .map((sample) => ({
          ...sample,
          sequenceOrder: sequenceByEntryId.get(sample.beerEntryId) ?? sample.sequenceOrder,
        }))
        .sort(
          (a, b) =>
            (a.sequenceOrder ?? Number.MAX_SAFE_INTEGER) -
            (b.sequenceOrder ?? Number.MAX_SAFE_INTEGER),
        ),
    );
    this.orderFixed.set(true);
    this.fixedByDisplayName.set(event.fixedByDisplayName);
    this.confirmingFix.set(false);
    this.fixError.set(null);
  }

  private handleFixError(error: unknown): void {
    const apiError = toGenericApiError(error);

    if (apiError.urn === 'urn:birrapoint:order-already-fixed') {
      const fixedBy = (apiError.extensions['fixedBy'] as string | null | undefined) ?? null;
      this.fixError.set(fixedBy ? `Order already fixed by ${fixedBy}.` : 'Order already fixed.');
      this.orderFixed.set(true);
      this.fixedByDisplayName.set(fixedBy);
      // We lost the race: reconcile the local order against whatever was actually fixed rather
      // than leaving the pre-race local reordering on screen.
      this.api.getTableSamples(this.tableId).subscribe({
        next: (samples) => this.samples.set(samples),
        error: () => {
          // Best-effort reconciliation: the order is locked either way; a failure here only
          // means the locally-visible order may briefly differ from the server's until reload.
        },
      });
      return;
    }

    if (apiError.urn === 'urn:birrapoint:invalid-state-transition') {
      this.fixError.set('This table is not open for ordering yet.');
      return;
    }

    this.fixError.set(errorMessage(apiError));
  }

  private handleCloseError(error: unknown): void {
    const apiError = toGenericApiError(error);

    if (apiError.urn === 'urn:birrapoint:table-closed') {
      // We lost the race: someone else at this table closed it first. That's the outcome this
      // judge wanted too, just not via their own click — treat it as success, not an error.
      this.tableClosed.set(true);
      return;
    }

    if (apiError.urn === 'urn:birrapoint:evaluations-incomplete') {
      const missing = (apiError.extensions['missing'] as string[] | undefined) ?? [];
      this.closeError.set(`Still needs evaluating: ${missing.join(', ')}.`);
      return;
    }

    if (apiError.urn === 'urn:birrapoint:discrepancy-open') {
      const blindCodes = (apiError.extensions['blindCodes'] as string[] | undefined) ?? [];
      this.closeError.set(`Unresolved discrepancies on: ${blindCodes.join(', ')}.`);
      return;
    }

    this.closeError.set(errorMessage(apiError));
  }
}
