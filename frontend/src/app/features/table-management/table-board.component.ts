import { CdkTrapFocus } from '@angular/cdk/a11y';
import type { CdkDragDrop } from '@angular/cdk/drag-drop';
import type { OnInit, WritableSignal } from '@angular/core';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { forkJoin } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { EntriesApiService } from '../../core/api/entries-api.service';
import type { EntryListItem } from '../../core/api/entries-api.service';
import { BpAlertComponent } from '../../shared/components/bp-alert/bp-alert.component';
import { BpButtonComponent } from '../../shared/components/bp-button/bp-button.component';
import { BpInputComponent } from '../../shared/components/bp-input/bp-input.component';
import { MesaCardComponent } from './mesa-card.component';
import { TableDetailModalComponent } from './table-detail-modal.component';
import type { DetailModalContent, TableOption } from './table-detail-modal.component';
import { TableManagementApiService } from './table-management-api.service';
import type {
  JudgeListItem,
  TableAssignmentRequest,
  TableMutationResult,
  TableSummary,
} from './table-management-api.service';
import {
  UNASSIGNED_BEERS_LIST_ID,
  UNASSIGNED_JUDGES_LIST_ID,
  UnassignedColumnComponent,
} from './unassigned-column.component';

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

function errorMessage(error: ApiError): string {
  return error.detail ?? error.title;
}

function toSummary(result: TableMutationResult): TableSummary {
  return {
    id: result.id,
    name: result.name,
    state: result.state,
    judges: result.judges,
    samples: result.samples,
    progress: result.progress,
    stats: result.stats,
  };
}

interface RawConflict {
  judgeId: string;
  beerEntryIds: string[];
}

export interface ResolvedConflict {
  judgeDisplayName: string;
  blindCodes: string[];
}

const JUDGES_PREFIX = 'judges-';
const BEERS_PREFIX = 'beers-';

function parseTableId(containerId: string, prefix: string, unassignedId: string): string | null {
  if (containerId === unassignedId) {
    return null;
  }
  return containerId.startsWith(prefix) ? containerId.slice(prefix.length) : null;
}

// T048/T048A/T048B/T048C: the organizer table-management board — "Unassigned" source column plus
// one MesaCard per tasting table, click-to-detail with a keyboard-accessible reassignment
// fallback, and CDK drag-and-drop between the unassigned column and any table (or between two
// tables). Every mutation goes through the same PUT /tables/{id} full-desired-state contract as
// entry-import/judge-management's mutation calls (contracts/rest-api.md §Tables).
//
// T123: extracted out of table-management.component.ts (now a thin route-bound wrapper around
// this component) into a route-agnostic, `input()`-driven component so wizard step 6 ("Mesas")
// can embed the exact same board instead of re-implementing table assignment.
@Component({
  selector: 'app-table-board',
  standalone: true,
  imports: [
    CdkTrapFocus,
    BpAlertComponent,
    BpButtonComponent,
    BpInputComponent,
    MesaCardComponent,
    UnassignedColumnComponent,
    TableDetailModalComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Table management</h1>

    @if (loadError(); as message) {
      <bp-alert type="error" title="No hemos podido cargar los datos">{{ message }}</bp-alert>
    }

    @if (bosWarning(); as message) {
      <bp-alert type="info" role="status">
        {{ message }}
        <bp-button
          type="button"
          label="Dismiss"
          variant="ghost"
          size="sm"
          (clicked)="dismissBosWarning()"
        ></bp-button>
      </bp-alert>
    }

    @if (dragError(); as message) {
      <bp-alert type="error" title="No hemos podido mover el elemento">{{ message }}</bp-alert>
    }

    <section aria-label="Add table" class="add-table">
      <bp-input
        id="new-table-name"
        label="New table name"
        [value]="newTableName()"
        (valueChange)="newTableName.set($event)"
      ></bp-input>
      @if (createError(); as message) {
        <bp-alert type="error" title="No hemos podido crear la mesa">{{ message }}</bp-alert>
      }
      <bp-button
        type="button"
        label="Add table"
        variant="primary"
        [loading]="creatingTable()"
        [disabled]="!newTableName().trim() || creatingTable()"
        (clicked)="onCreateTable()"
      ></bp-button>
    </section>

    <div class="table-management-layout">
      <app-unassigned-column
        [judges]="unassignedJudges()"
        [beers]="unassignedBeers()"
        [connectedJudgeListIds]="judgeDropListIds()"
        [connectedBeerListIds]="beerDropListIds()"
        (judgeActivated)="onJudgeClicked($event)"
        (beerActivated)="onBeerClicked($event)"
        (judgesDropped)="onJudgesDropped($event)"
        (beersDropped)="onBeersDropped($event)"
      />

      @for (table of tables(); track table.id) {
        <app-mesa-card
          [table]="table"
          [connectedJudgeListIds]="judgeDropListIds()"
          [connectedBeerListIds]="beerDropListIds()"
          (judgeActivated)="onJudgeClicked($event)"
          (beerActivated)="onBeerClicked($event)"
          (judgesDropped)="onJudgesDropped($event)"
          (beersDropped)="onBeersDropped($event)"
        />
      }
    </div>

    @if (conflictDialog(); as conflicts) {
      <div class="modal-backdrop" role="presentation" (click)="dismissConflictDialog()">
        <div
          role="alertdialog"
          aria-modal="true"
          aria-label="Conflict of interest"
          class="modal-panel"
          cdkTrapFocus
          cdkTrapFocusAutoCapture
          (click)="$event.stopPropagation()"
          (keydown.escape)="dismissConflictDialog()"
        >
          <h2>Conflict of interest</h2>
          <ul>
            @for (conflict of conflicts; track conflict.judgeDisplayName) {
              <li>
                {{ conflict.judgeDisplayName }} conflicts with: {{ conflict.blindCodes.join(', ') }}
              </li>
            }
          </ul>
          <bp-button
            type="button"
            label="Close"
            variant="secondary"
            (clicked)="dismissConflictDialog()"
          ></bp-button>
        </div>
      </div>
    }

    @if (selectedDetail(); as detail) {
      <app-table-detail-modal
        [content]="detail"
        [assignedTableIds]="selectedAssignedTableIds()"
        [tables]="tableOptions()"
        (closed)="selectedDetail.set(null)"
        (move)="onModalMove($event)"
      />
    }
  `,
  styles: `
    h1 {
      font-family: 'Fraunces', serif;
      font-size: 1.5rem;
      font-weight: 600;
      color: var(--color-bp-text);
      margin: 0 0 var(--spacing-6);
    }

    .add-table {
      display: flex;
      align-items: flex-start;
      gap: var(--spacing-4);
      margin-bottom: var(--spacing-6);
    }

    .add-table bp-input {
      flex: 1 1 auto;
      max-width: 20rem;
    }

    .table-management-layout {
      display: flex;
      gap: var(--spacing-6);
      align-items: flex-start;
      flex-wrap: wrap;
      margin-top: var(--spacing-4);
    }

    .modal-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(4, 23, 18, 0.45);
      display: flex;
      align-items: center;
      justify-content: center;
      padding: var(--spacing-4);
      z-index: 10;
    }

    .modal-panel {
      background: var(--color-bp-surface);
      border-radius: var(--radius-lg);
      box-shadow: var(--shadow-lg);
      padding: var(--spacing-6);
      min-width: 20rem;
    }

    .modal-panel h2 {
      font-family: 'Fraunces', serif;
      font-size: 1.25rem;
      margin: 0 0 var(--spacing-3);
      color: var(--color-bp-text);
    }
  `,
})
export class TableBoardComponent implements OnInit {
  private readonly api = inject(TableManagementApiService);
  private readonly entriesApi = inject(EntriesApiService);

  readonly competitionId = input.required<string>();

  protected readonly tables = signal<TableSummary[]>([]);
  protected readonly entries = signal<EntryListItem[]>([]);
  protected readonly judges = signal<JudgeListItem[]>([]);
  protected readonly loadError = signal<string | null>(null);

  protected readonly newTableName = signal('');
  protected readonly creatingTable = signal(false);
  protected readonly createError = signal<string | null>(null);

  protected readonly dragError = signal<string | null>(null);
  protected readonly bosWarning = signal<string | null>(null);
  protected readonly conflictDialog = signal<ResolvedConflict[] | null>(null);

  protected readonly selectedDetail = signal<DetailModalContent | null>(null);

  protected readonly unassignedJudges = computed(() => {
    const assignedIds = new Set(this.tables().flatMap((table) => table.judges.map((j) => j.id)));
    return this.judges().filter((judge) => !assignedIds.has(judge.id));
  });

  protected readonly unassignedBeers = computed(() =>
    this.entries().filter((entry) => entry.tastingTableId === null),
  );

  protected readonly judgeDropListIds = computed(() => [
    UNASSIGNED_JUDGES_LIST_ID,
    ...this.tables().map((table) => `${JUDGES_PREFIX}${table.id}`),
  ]);

  protected readonly beerDropListIds = computed(() => [
    UNASSIGNED_BEERS_LIST_ID,
    ...this.tables().map((table) => `${BEERS_PREFIX}${table.id}`),
  ]);

  protected readonly tableOptions = computed<TableOption[]>(() =>
    this.tables().map((table) => ({ id: table.id, name: table.name })),
  );

  // input.required() is only guaranteed to be resolved starting from ngOnInit -- reading it in
  // the constructor throws NG0950 (and would silently race in production, since the constructor
  // runs before Angular finishes binding template inputs onto this instance).
  ngOnInit(): void {
    this.loadAll();
  }

  private loadAll(): void {
    this.loadError.set(null);
    forkJoin({
      tables: this.api.getTables(this.competitionId()),
      entries: this.entriesApi.getEntries(this.competitionId()),
      judges: this.api.getJudges(this.competitionId()),
    }).subscribe({
      next: ({ tables, entries, judges }) => {
        this.tables.set(tables);
        this.entries.set(entries);
        this.judges.set(judges);
      },
      error: (error: unknown) => this.loadError.set(errorMessage(toGenericApiError(error))),
    });
  }

  protected onCreateTable(): void {
    const name = this.newTableName().trim();
    if (!name || this.creatingTable()) {
      return;
    }

    this.creatingTable.set(true);
    this.createError.set(null);

    this.api.createTable(this.competitionId(), { name, judgeIds: [], beerEntryIds: [] }).subscribe({
      next: (result) => {
        this.creatingTable.set(false);
        this.newTableName.set('');
        this.tables.update((tables) => [...tables, toSummary(result)]);
        this.showBosWarningIfAny(result.bosFlaggedEntryIds);
      },
      error: (error: unknown) => {
        this.creatingTable.set(false);
        this.handleMutationError(error, this.createError);
      },
    });
  }

  protected onJudgeClicked(judgeId: string): void {
    const judge = this.judges().find((j) => j.id === judgeId);
    if (!judge) {
      return;
    }
    this.selectedDetail.set({
      kind: 'judge',
      id: judge.id,
      displayName: judge.displayName,
      email: judge.email,
    });
  }

  protected onBeerClicked(entryId: string): void {
    const entry = this.entries().find((e) => e.id === entryId);
    if (!entry) {
      return;
    }
    this.selectedDetail.set({
      kind: 'beer',
      id: entry.id,
      blindCode: entry.blindCode,
      styleName: entry.styleName,
      abvPercent: entry.abvPercent,
      abvLow: entry.abvLow,
      abvHigh: entry.abvHigh,
    });
  }

  // Set-membership computation, not a hardcoded single assignment (a judge can theoretically be
  // seated at more than one table; a beer's uniqueness is enforced server-side but computed the
  // same way for symmetry).
  protected selectedAssignedTableIds(): string[] {
    const detail = this.selectedDetail();
    if (!detail) {
      return [];
    }
    if (detail.kind === 'judge') {
      return this.tables()
        .filter((table) => table.judges.some((j) => j.id === detail.id))
        .map((table) => table.id);
    }
    const entry = this.entries().find((e) => e.id === detail.id);
    return entry?.tastingTableId ? [entry.tastingTableId] : [];
  }

  // Keyboard-accessible equivalent of T048B's drag-and-drop reassignment (invoked from the detail
  // modal's "Move to" control).
  protected onModalMove(targetTableId: string | null): void {
    const detail = this.selectedDetail();
    if (!detail) {
      return;
    }
    const fromTableIds = this.selectedAssignedTableIds();
    this.selectedDetail.set(null);

    if (detail.kind === 'judge') {
      this.moveJudge(detail.id, fromTableIds, targetTableId);
    } else {
      this.moveBeer(detail.id, fromTableIds[0] ?? null, targetTableId);
    }
  }

  protected onJudgesDropped(event: CdkDragDrop<unknown>): void {
    if (event.previousContainer === event.container) {
      return;
    }
    const judgeId = event.item.data as string;
    const fromTableId = parseTableId(
      event.previousContainer.id,
      JUDGES_PREFIX,
      UNASSIGNED_JUDGES_LIST_ID,
    );
    const toTableId = parseTableId(event.container.id, JUDGES_PREFIX, UNASSIGNED_JUDGES_LIST_ID);
    this.moveJudge(judgeId, fromTableId ? [fromTableId] : [], toTableId);
  }

  protected onBeersDropped(event: CdkDragDrop<unknown>): void {
    if (event.previousContainer === event.container) {
      return;
    }
    const entryId = event.item.data as string;
    const fromTableId = parseTableId(
      event.previousContainer.id,
      BEERS_PREFIX,
      UNASSIGNED_BEERS_LIST_ID,
    );
    const toTableId = parseTableId(event.container.id, BEERS_PREFIX, UNASSIGNED_BEERS_LIST_ID);
    this.moveBeer(entryId, fromTableId, toTableId);
  }

  // Removes from every source table first (sequentially — a judge in >1 table is only reachable
  // via this same "Move to" path, which always fully replaces membership), then adds to the
  // target. Each leg only mutates local state once its own PUT resolves, so a mid-flight failure
  // never leaves local state ahead of the server: the caller sees exactly what actually committed.
  private moveJudge(judgeId: string, fromTableIds: string[], toTableId: string | null): void {
    this.dragError.set(null);
    this.removeJudgeFromTables(judgeId, fromTableIds, 0, () => {
      if (!toTableId || fromTableIds.includes(toTableId)) {
        return;
      }
      this.addJudgeToTable(judgeId, toTableId);
    });
  }

  private removeJudgeFromTables(
    judgeId: string,
    tableIds: string[],
    index: number,
    onDone: () => void,
  ): void {
    if (index >= tableIds.length) {
      onDone();
      return;
    }
    const current = this.tables().find((table) => table.id === tableIds[index]);
    if (!current) {
      this.removeJudgeFromTables(judgeId, tableIds, index + 1, onDone);
      return;
    }

    const request: TableAssignmentRequest = {
      name: current.name,
      judgeIds: current.judges.filter((j) => j.id !== judgeId).map((j) => j.id),
      beerEntryIds: current.samples.map((s) => s.beerEntryId),
    };
    this.api.updateTable(this.competitionId(), current.id, request).subscribe({
      next: (result) => {
        this.applyMutationResult(result);
        this.removeJudgeFromTables(judgeId, tableIds, index + 1, onDone);
      },
      error: (error: unknown) => this.handleMutationError(error, this.dragError),
    });
  }

  private addJudgeToTable(judgeId: string, tableId: string): void {
    const current = this.tables().find((table) => table.id === tableId);
    if (!current) {
      return;
    }
    const request: TableAssignmentRequest = {
      name: current.name,
      judgeIds: [...current.judges.map((j) => j.id), judgeId],
      beerEntryIds: current.samples.map((s) => s.beerEntryId),
    };
    this.api.updateTable(this.competitionId(), tableId, request).subscribe({
      next: (result) => this.applyMutationResult(result),
      error: (error: unknown) => this.handleMutationError(error, this.dragError),
    });
  }

  // Cross-table beer move needs two sequential PUTs (remove from source's set, then add to
  // target's) — the add only fires once the remove has actually committed, so a failure on the
  // add leg leaves the beer correctly unassigned rather than duplicated or lost.
  private moveBeer(entryId: string, fromTableId: string | null, toTableId: string | null): void {
    this.dragError.set(null);
    if (fromTableId === toTableId) {
      return;
    }

    if (!fromTableId) {
      if (toTableId) {
        this.addBeerToTable(entryId, toTableId);
      }
      return;
    }

    const current = this.tables().find((table) => table.id === fromTableId);
    if (!current) {
      return;
    }
    const request: TableAssignmentRequest = {
      name: current.name,
      judgeIds: current.judges.map((j) => j.id),
      beerEntryIds: current.samples.map((s) => s.beerEntryId).filter((id) => id !== entryId),
    };
    this.api.updateTable(this.competitionId(), fromTableId, request).subscribe({
      next: (result) => {
        this.applyMutationResult(result);
        if (toTableId) {
          this.addBeerToTable(entryId, toTableId);
        }
      },
      error: (error: unknown) => this.handleMutationError(error, this.dragError),
    });
  }

  private addBeerToTable(entryId: string, tableId: string): void {
    const current = this.tables().find((table) => table.id === tableId);
    if (!current) {
      return;
    }
    const request: TableAssignmentRequest = {
      name: current.name,
      judgeIds: current.judges.map((j) => j.id),
      beerEntryIds: [...current.samples.map((s) => s.beerEntryId), entryId],
    };
    this.api.updateTable(this.competitionId(), tableId, request).subscribe({
      next: (result) => this.applyMutationResult(result),
      error: (error: unknown) => this.handleMutationError(error, this.dragError),
    });
  }

  // The mutation response is the authoritative membership for this one table — reconcile
  // `tables` from it directly. `entries` is refetched wholesale instead of patched incrementally:
  // FR-018's BOS flag/unflag is competition-wide and can touch entries far outside this table's
  // own membership (an entry at a different table, or still unassigned), and the mutation
  // response only reports newly-*flagged* ids, never newly-*unflagged* ones — so a full refetch
  // is the only way to stay correct in both directions rather than silently going stale until the
  // next page load (found by T049's E2E: the BOS banner announced correctly but the flagged
  // token's visual state didn't update without a reload).
  private applyMutationResult(result: TableMutationResult): void {
    const summary = toSummary(result);
    this.tables.update((tables) => {
      const index = tables.findIndex((table) => table.id === summary.id);
      if (index === -1) {
        return [...tables, summary];
      }
      const next = [...tables];
      next[index] = summary;
      return next;
    });

    this.entriesApi.getEntries(this.competitionId()).subscribe({
      next: (entries) => this.entries.set(entries),
      error: (error: unknown) => this.handleMutationError(error, this.dragError),
    });

    this.showBosWarningIfAny(result.bosFlaggedEntryIds);
  }

  private showBosWarningIfAny(bosFlaggedEntryIds: string[]): void {
    if (bosFlaggedEntryIds.length === 0) {
      return;
    }
    const count = bosFlaggedEntryIds.length;
    this.bosWarning.set(`${count} ${count === 1 ? 'entry' : 'entries'} flagged Not Valid for BOS.`);
  }

  protected dismissBosWarning(): void {
    this.bosWarning.set(null);
  }

  protected dismissConflictDialog(): void {
    this.conflictDialog.set(null);
  }

  private handleMutationError(error: unknown, messageSignal: WritableSignal<string | null>): void {
    const apiError = toGenericApiError(error);
    if (apiError.urn === 'urn:birrapoint:conflict-of-interest') {
      const rawConflicts = (apiError.extensions['conflicts'] as RawConflict[] | undefined) ?? [];
      this.conflictDialog.set(this.resolveConflicts(rawConflicts));
      return;
    }
    messageSignal.set(errorMessage(apiError));
  }

  private resolveConflicts(rawConflicts: RawConflict[]): ResolvedConflict[] {
    const judgesById = new Map(this.judges().map((judge) => [judge.id, judge]));
    const entriesById = new Map(this.entries().map((entry) => [entry.id, entry]));
    return rawConflicts.map((conflict) => ({
      judgeDisplayName: judgesById.get(conflict.judgeId)?.displayName ?? conflict.judgeId,
      blindCodes: conflict.beerEntryIds.map((id) => entriesById.get(id)?.blindCode ?? id),
    }));
  }
}
