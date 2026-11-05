import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { INTENSITY_LABELS } from '../evaluation-descriptor-catalog';

// Session 2026-09-21 (FR-063): the paper sheet's 4-stop discrete intensity rating (Nada/Bajo/
// Medio/Alto = 0–3) for descriptors like Malta/Lúpulos/Fermentación/Cuerpo. Plain signal-based
// [value]/(valueChange) binding — not a ControlValueAccessor/formControlName — matching this
// codebase's established convention for optional, non-validated fields (see e.g.
// categories-step.component.ts's own manual <select> bindings): every descriptor field here is
// advisory (never gates submit), so it doesn't need reactive-forms' validity machinery.
//
// Organizer follow-up (same session): every descriptor now needs an "Inapropiado para el estilo"
// flag, but repeating that full phrase as a separate row under every one of the ~17 sliders on
// the sheet was cluttered. The checkbox is now built into the slider itself — a compact toggle on
// the same line as the label, labelled just "Inapropiado" (the attribute name right next to it
// already gives the context "para el estilo" doesn't need to repeat) — one visual pattern, not a
// wall of near-duplicate text.
@Component({
  selector: 'bp-discrete-slider',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  // A static id="x" attribute on <bp-discrete-slider> is reflected by Angular onto this host
  // element too, in addition to feeding the id() input below — duplicating the id the inner
  // <input id="x"> needs to itself be uniquely addressable (same fix as bp-input.component.ts).
  host: { '[attr.id]': 'null' },
  template: `
    <div class="discrete-slider">
      <div class="discrete-slider__header">
        <span class="discrete-slider__label">{{ label() }}</span>
        <label class="discrete-slider__inappropriate">
          <input
            type="checkbox"
            [checked]="inappropriate()"
            [attr.aria-label]="label() + ': inapropiado para el estilo'"
            (change)="onInappropriateChange($event)"
          />
          Inapropiado
        </label>
      </div>
      <input
        type="range"
        min="0"
        max="3"
        step="1"
        [id]="id()"
        [attr.aria-label]="label()"
        [attr.aria-valuetext]="intensityLabels[value()]"
        [value]="value()"
        (input)="onInput($event)"
      />
      <div class="discrete-slider__ticks" aria-hidden="true">
        @for (tick of intensityLabels; track tick) {
          <span>{{ tick }}</span>
        }
      </div>
    </div>
  `,
  styles: `
    .discrete-slider {
      margin: var(--spacing-3) 0;
    }

    .discrete-slider__header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--spacing-2) var(--spacing-3);
      margin-bottom: var(--spacing-2);
    }

    .discrete-slider__label {
      font-size: 0.875rem;
      font-weight: 600;
      color: var(--color-bp-text);
    }

    .discrete-slider__inappropriate {
      flex: none;
      display: flex;
      align-items: center;
      gap: var(--spacing-1);
      font-size: 0.75rem;
      font-weight: 500;
      color: var(--color-bp-text-muted);
      white-space: nowrap;
    }

    input[type='range'] {
      width: 100%;
      accent-color: var(--color-bp-cobre-500);
      min-height: 44px;
    }

    .discrete-slider__ticks {
      display: flex;
      justify-content: space-between;
      font-size: 0.75rem;
      color: var(--color-bp-text-muted);
      padding: 0 2px;
    }
  `,
})
export class BpDiscreteSliderComponent {
  readonly id = input.required<string>();
  readonly label = input.required<string>();
  readonly value = input(0);
  readonly inappropriate = input(false);
  readonly valueChange = output<number>();
  readonly inappropriateChange = output<boolean>();

  protected readonly intensityLabels = INTENSITY_LABELS;

  protected onInput(event: Event): void {
    this.valueChange.emit(Number((event.target as HTMLInputElement).value));
  }

  protected onInappropriateChange(event: Event): void {
    this.inappropriateChange.emit((event.target as HTMLInputElement).checked);
  }
}
