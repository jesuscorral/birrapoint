import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { TableBoardComponent } from '../../table-management/table-board.component';
import { BpStepActionsComponent } from '../../../shared/components/bp-step-actions/bp-step-actions.component';

// T123: wizard step 6 ("Mesas") — a thin wrapper embedding the already-fully-built,
// route-agnostic TableBoardComponent, following the same input/output contract every other
// wizard step uses. This is the last step, so its forward action reads "Finalizar" and leaves the
// wizard rather than advancing. It emits rather than navigating: the shell owns the FR-007
// unsaved-changes prompt, so "Finalizar" and the header's "Volver al listado" take the same guarded
// path out.
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
      (statusChange)="statusChange.emit($event)"
    />

    <!-- Last step, so the forward action closes the wizard instead of advancing. Same bar, same
         places — only the label changes. -->
    <bp-step-actions
      nextLabel="Finalizar"
      [sticky]="false"
      (back)="back.emit()"
      (next)="finished.emit()"
    />
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
  readonly competitionId = input.required<string>();
  readonly back = output<void>();
  // "Finalizar" — handled by the wizard shell via onRequestExit(), so an un-submitted "Add table"
  // name still raises the FR-007 stay-or-discard prompt instead of being dropped on the way out.
  readonly finished = output<void>();
  // Forwarded straight from app-table-board's own dirtyChange (see table-board.component.ts):
  // the only in-progress, un-persisted state this step can hold is an un-submitted "Add table"
  // name -- every other mutation (drag-drop, click-to-detail "Move to") saves immediately via
  // the API.
  readonly dirtyChange = output<boolean>();
  // Forwarded straight from app-table-board's own statusChange, for the wizard stepper marker.
  readonly statusChange = output<'complete' | 'partial'>();
}
