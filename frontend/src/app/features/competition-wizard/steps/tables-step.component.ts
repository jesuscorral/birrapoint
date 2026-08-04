import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { Router } from '@angular/router';

import { TableBoardComponent } from '../../table-management/table-board.component';
import { BpButtonComponent } from '../../../shared/components/bp-button/bp-button.component';

// T123: wizard step 6 ("Mesas") — a thin wrapper embedding the already-fully-built,
// route-agnostic TableBoardComponent, following the same input/output contract every other
// wizard step uses. Unlike the other steps there's no `saved` output: this is the last step, and
// its own terminal button navigates away directly (same pattern judge-import-step used before
// this step existed).
@Component({
  selector: 'app-tables-step',
  imports: [TableBoardComponent, BpButtonComponent],
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

    <div class="step-actions">
      <bp-button type="button" label="← Volver" variant="ghost" (clicked)="back.emit()"></bp-button>
      <bp-button
        type="button"
        label="Ir al panel de organizador"
        variant="primary"
        (clicked)="goToDashboard()"
      ></bp-button>
    </div>
  `,
  styles: [
    `
      .step-lead {
        margin: 0 0 var(--spacing-6);
        color: var(--color-bp-text-muted);
        font-size: 0.9375rem;
      }

      .step-actions {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin: var(--spacing-8) calc(-1 * var(--spacing-8)) calc(-1 * var(--spacing-8));
        padding: var(--spacing-4) var(--spacing-8) var(--spacing-6);
        border-top: 1px solid var(--color-bp-border);
        position: sticky;
        bottom: 0;
        background: var(--color-bp-surface);
        z-index: 1;
      }

      @media (max-width: 640px) {
        .step-actions {
          margin: var(--spacing-8) calc(-1 * var(--spacing-6)) calc(-1 * var(--spacing-6));
          padding: var(--spacing-4) var(--spacing-6) var(--spacing-6);
        }
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
