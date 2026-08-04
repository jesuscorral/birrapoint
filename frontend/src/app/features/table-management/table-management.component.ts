import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { TableBoardComponent } from './table-board.component';

// T123: thin route-bound wrapper — all table-assignment logic now lives in TableBoardComponent
// (route-agnostic, `input()`-driven) so it can be reused unchanged inside the competition wizard's
// "Mesas" step. This component's only job is reading the :id route param and handing it off.
@Component({
  selector: 'app-table-management',
  standalone: true,
  imports: [TableBoardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-table-board [competitionId]="competitionId" />`,
})
export class TableManagementComponent {
  private readonly route = inject(ActivatedRoute);
  protected readonly competitionId = this.route.snapshot.paramMap.get('id')!;
}
