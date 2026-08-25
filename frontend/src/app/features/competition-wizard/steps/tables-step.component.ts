import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { Router } from '@angular/router';

import { TableBoardComponent } from '../../table-management/table-board.component';
import { BpStepActionsComponent } from '../../../shared/components/bp-step-actions/bp-step-actions.component';

// T123: wizard step 6 ("Mesas") — a thin wrapper embedding the already-fully-built,
// route-agnostic TableBoardComponent, following the same input/output contract every other
// wizard step uses. Unlike the other steps there's no `saved` output: this is the last step, and
// its own terminal button navigates away directly (same pattern judge-import-step used before
// this step existed).
@Component({
  selector: 'app-tables-step',
  imports: [TableBoardComponent, BpStepActionsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="step-lead">
      Crea las mesas de cata, asigna jueces y cervezas a cada una, y comprueba el balance de estilos
      y grado alcohólico antes de arrancar la competición.
    </p>

    <app-table-board
      [competitionId]="competitionId()"
      [headingLevel]="2"
      [heading]="'Mesas'"
      (dirtyChange)="dirtyChange.emit($event)"
    />

    <!-- Last step, so the forward action closes the wizard instead of advancing. Same bar, same
         places — only the label changes. -->
    <bp-step-actions nextLabel="Finalizar" (back)="back.emit()" (next)="goToDashboard()" />
  `,
  styles: [
    `
      .step-lead {
        margin: 0 0 var(--spacing-6);
        color: var(--color-bp-text-muted);
        font-size: 0.9375rem;
      }
    `,
  ],
})
export class TablesStepComponent {
  private readonly router = inject(Router);

  readonly competitionId = input.required<string>();
  readonly back = output<void>();
  // Forwarded straight from app-table-board's own dirtyChange (see table-board.component.ts):
  // the only in-progress, un-persisted state this step can hold is an un-submitted "Add table"
  // name -- every other mutation (drag-drop, click-to-detail "Move to") saves immediately via
  // the API.
  readonly dirtyChange = output<boolean>();

  protected goToDashboard(): void {
    this.router.navigateByUrl('/organizer/dashboard');
  }
}
