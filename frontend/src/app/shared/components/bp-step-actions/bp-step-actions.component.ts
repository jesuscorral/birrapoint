import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { BpButtonComponent } from '../bp-button/bp-button.component';

// T125: the wizard's six steps each grew their own action bar, and they drifted — step 1 had a raw
// <button> link where the others had a ghost bp-button, step 2's primary said "Guardar borrador"
// where every other step said "Continuar", step 3 carried two different back affordances side by
// side, and step 6 had no forward action at all. This is the single bar all of them now render.
//
// Three fixed zones, always in the same place: Atrás on the left, the step's own actions in the
// middle (projected content — "Guardar borrador", "Subir archivo", "Consolidar", …), and the
// forward action on the right. A step only chooses its labels and what goes in the middle; it
// never re-decides the layout.
@Component({
  selector: 'bp-step-actions',
  standalone: true,
  imports: [BpButtonComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="step-actions">
      <div class="step-actions__zone step-actions__zone--start">
        @if (showBack()) {
          <bp-button
            type="button"
            [label]="backLabel()"
            variant="ghost"
            [disabled]="backDisabled()"
            (clicked)="back.emit()"
          ></bp-button>
        }
      </div>

      <div class="step-actions__zone step-actions__zone--center">
        <ng-content />
      </div>

      <div class="step-actions__zone step-actions__zone--end">
        @if (showNext()) {
          <bp-button
            [type]="nextType()"
            [label]="nextLabel()"
            variant="primary"
            [loading]="nextLoading()"
            [disabled]="nextDisabled()"
            (clicked)="next.emit()"
          ></bp-button>
        }
      </div>
    </div>
  `,
  styles: `
    /* Negative margins pull the bar out to the wizard card's own edges so it reads as the card's
       footer rather than a floating row inside it; the host card sets --bp-step-actions-inset to
       its own horizontal padding. */
    .step-actions {
      display: grid;
      grid-template-columns: 1fr auto 1fr;
      align-items: center;
      gap: var(--spacing-4);
      margin: var(--spacing-8) calc(-1 * var(--bp-step-actions-inset, var(--spacing-8)))
        calc(-1 * var(--bp-step-actions-inset, var(--spacing-8)));
      padding: var(--spacing-4) var(--bp-step-actions-inset, var(--spacing-8)) var(--spacing-6);
      border-top: 1px solid var(--color-bp-border);
      position: sticky;
      bottom: 0;
      background: var(--color-bp-surface);
      z-index: 2;
    }

    .step-actions__zone {
      display: flex;
      align-items: center;
      gap: var(--spacing-3);
      flex-wrap: wrap;
    }

    .step-actions__zone--center {
      justify-content: center;
    }

    /* Keeps "Siguiente" hard against the right edge even when the centre zone is empty. */
    .step-actions__zone--end {
      justify-content: flex-end;
    }

    @media (max-width: 640px) {
      .step-actions {
        grid-template-columns: 1fr;
        justify-items: stretch;
        gap: var(--spacing-2);
        margin: var(--spacing-8) calc(-1 * var(--bp-step-actions-inset, var(--spacing-6)))
          calc(-1 * var(--bp-step-actions-inset, var(--spacing-6)));
        padding: var(--spacing-4) var(--bp-step-actions-inset, var(--spacing-6)) var(--spacing-6);
      }

      /* Forward action first in source-visual order on narrow screens, where the row becomes a
         stack and the primary action should be the one under the thumb. */
      .step-actions__zone--end {
        order: -1;
        justify-content: stretch;
      }

      .step-actions__zone--center,
      .step-actions__zone--start {
        justify-content: stretch;
      }
    }
  `,
})
export class BpStepActionsComponent {
  readonly showBack = input(true);
  readonly backLabel = input('Atrás');
  readonly backDisabled = input(false);

  readonly showNext = input(true);
  readonly nextLabel = input('Siguiente');
  readonly nextDisabled = input(false);
  readonly nextLoading = input(false);
  // `submit` lets a step keep its existing (ngSubmit) form wiring instead of duplicating the
  // submit path behind a click handler.
  readonly nextType = input<'button' | 'submit'>('button');

  readonly back = output<void>();
  readonly next = output<void>();
}
