import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { CompetitionsApiService } from '../../core/api/competitions-api.service';
import type { CompetitionState } from '../../core/api/competitions-api.service';
import { BpPageShellComponent } from '../../shared/components/bp-page-shell/bp-page-shell.component';
import { TableBoardComponent } from './table-board.component';

// FR-061 / Session 2026-09-19 clarification: the wizard/standalone table-management route is
// read-only once the competition has moved past Active.
const READ_ONLY_STATES: ReadonlySet<CompetitionState> = new Set(['InEvaluation', 'Finalized']);

// T123: thin route-bound wrapper — all table-assignment logic now lives in TableBoardComponent
// (route-agnostic, `input()`-driven) so it can be reused unchanged inside the competition wizard's
// "Mesas" step. This component's only job is reading the :id route param and handing it off.
//
// T127: wrapped in the shared page shell (topbar + main landmark), and now also resolves the
// competition's own state to decide whether the board should render read-only. The board renders
// immediately rather than waiting on this fetch — it defaults editable until the state is known,
// then flips to read-only if it turns out to be InEvaluation/Finalized, since blocking the whole
// screen on a state check unrelated to table assignment itself would slow down the common case.
@Component({
  selector: 'app-table-management',
  standalone: true,
  imports: [BpPageShellComponent, TableBoardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <bp-page-shell>
      <app-table-board [competitionId]="competitionId" [readOnly]="readOnly()" />
    </bp-page-shell>
  `,
})
export class TableManagementComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly competitionsApi = inject(CompetitionsApiService);

  protected readonly competitionId = this.route.snapshot.paramMap.get('id')!;
  protected readonly readOnly = signal(false);

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
}
