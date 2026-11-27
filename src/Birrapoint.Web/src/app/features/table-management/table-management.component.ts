import type { OnDestroy, OnInit } from '@angular/core';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import type { Subscription } from 'rxjs';

import { CompetitionsApiService } from '../../core/api/competitions-api.service';
import type { CompetitionState } from '../../core/api/competitions-api.service';
import { CompetitionHubService } from '../../core/realtime/competition-hub.service';
import type { JudgeConflictDetectedEvent } from '../../core/realtime/competition-hub.events';
import { JudgeConflictBannerComponent } from '../../shared/components';
import type { JudgeConflictBannerItem } from '../../shared/components';
import { BpPageShellComponent } from '../../core/layout/bp-page-shell/bp-page-shell.component';
import { TableBoardComponent } from './table-board.component';

// FR-061 / Session 2026-09-19 clarification: the wizard/standalone table-management route is
// read-only once the competition has moved past Active.
const READ_ONLY_STATES: ReadonlySet<CompetitionState> = new Set(['InEvaluation', 'Finalized']);

// Thin route-bound wrapper — all table-assignment logic now lives in TableBoardComponent
// (route-agnostic, `input()`-driven) so it can be reused unchanged inside the competition wizard's
// "Mesas" step. This component's only job is reading the :id route param and handing it off.
//
// Wrapped in the shared page shell (topbar + main landmark), and now also resolves the
// competition's own state to decide whether the board should render read-only. The board renders
// immediately rather than waiting on this fetch — it defaults editable until the state is known,
// then flips to read-only if it turns out to be InEvaluation/Finalized, since blocking the whole
// screen on a state check unrelated to table assignment itself would slow down the common case.
@Component({
  selector: 'app-table-management',
  standalone: true,
  imports: [BpPageShellComponent, TableBoardComponent, JudgeConflictBannerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <bp-page-shell>
      <bp-judge-conflict-banner
        [conflicts]="conflictBanners()"
        (dismissed)="onDismissConflict($event)"
      />
      <app-table-board [competitionId]="competitionId" [readOnly]="readOnly()" />
    </bp-page-shell>
  `,
})
export class TableManagementComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly competitionsApi = inject(CompetitionsApiService);
  private readonly hub = inject(CompetitionHubService);
  private conflictSubscription: Subscription | null = null;

  protected readonly competitionId = this.route.snapshot.paramMap.get('id')!;
  protected readonly readOnly = signal(false);
  // Keyed by table: a repeat JudgeConflictDetected for the same table replaces its banner. The
  // board owns the table list, so the name is unknown here and the banner says "una mesa".
  private readonly judgeConflicts = signal<Map<string, JudgeConflictDetectedEvent>>(new Map());
  protected readonly conflictBanners = computed<JudgeConflictBannerItem[]>(() =>
    [...this.judgeConflicts().values()].map((event) => ({
      tableId: event.tableId,
      judgeDisplayName: event.judgeDisplayName,
      tableName: null,
    })),
  );

  constructor() {
    this.competitionsApi.getById(this.competitionId).subscribe({
      next: (competition) => this.readOnly.set(READ_ONLY_STATES.has(competition.state)),
      // Best-effort: a failed state check leaves the board editable rather than blocking table
      // assignment on a lookup that isn't itself part of that workflow.
      error: () => {
        this.readOnly.set(false);
      },
    });
  }

  ngOnInit(): void {
    void this.connectToHub();
  }

  ngOnDestroy(): void {
    this.conflictSubscription?.unsubscribe();
    void this.hub.leaveCompetition(this.competitionId).catch(() => {
      // Best-effort: leaving on navigation-away is a courtesy, not a functional requirement.
    });
  }

  protected onDismissConflict(tableId: string): void {
    this.judgeConflicts.update((conflicts) => {
      const next = new Map(conflicts);
      next.delete(tableId);
      return next;
    });
  }

  private async connectToHub(): Promise<void> {
    try {
      await this.hub.start();
      await this.hub.joinCompetitionAsOrganizer(this.competitionId);
      this.conflictSubscription = this.hub
        .on('JudgeConflictDetected')
        .subscribe((event) =>
          this.judgeConflicts.update((conflicts) => new Map(conflicts).set(event.tableId, event)),
        );
    } catch {
      // Realtime is best-effort (contracts/signalr-hub.md): the screen works over REST without it.
    }
  }
}
