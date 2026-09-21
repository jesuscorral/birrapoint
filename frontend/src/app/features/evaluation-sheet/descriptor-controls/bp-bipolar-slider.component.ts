import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

// Session 2026-09-21 (FR-063): the paper sheet's continuous bipolar sliders — Retención (Baja↔
// Alta), Equilibrio (Lupulado↔Maltoso), Final/Retrogusto (Seco↔Dulce), and Impresión General's
// three axes. Same plain signal-based binding convention as BpDiscreteSliderComponent (see its
// own doc comment) — advisory data, no reactive-forms validity needed. Same compact inline
// "Inapropiado" toggle too — see BpDiscreteSliderComponent's doc comment for why it lives in the
// header row instead of a separate line.
@Component({
  selector: 'bp-bipolar-slider',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  // A static id="x" attribute on <bp-bipolar-slider> is reflected by Angular onto this host
  // element too, in addition to feeding the id() input below — duplicating the id the inner
  // <input id="x"> needs to itself be uniquely addressable (same fix as bp-input.component.ts).
  host: { '[attr.id]': 'null' },
  template: `
    <div class="bipolar-slider">
      <div class="bipolar-slider__header">
        <span class="bipolar-slider__label">{{ label() }}</span>
        <label class="bipolar-slider__inappropriate">
          <input
            type="checkbox"
            [checked]="inappropriate()"
            (change)="onInappropriateChange($event)"
          />
          Inapropiado
        </label>
      </div>
      <div class="bipolar-slider__track">
        <span class="bipolar-slider__pole">{{ startLabel() }}</span>
        <input
          type="range"
          min="0"
          max="100"
          step="1"
          [id]="id()"
          [attr.aria-label]="label()"
          [attr.aria-valuetext]="value() + ' de 100, entre ' + startLabel() + ' y ' + endLabel()"
          [value]="value()"
          (input)="onInput($event)"
        />
        <span class="bipolar-slider__pole">{{ endLabel() }}</span>
      </div>
    </div>
  `,
  styles: `
    .bipolar-slider {
      margin: var(--spacing-3) 0;
    }

    .bipolar-slider__header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--spacing-3);
      margin-bottom: var(--spacing-2);
    }

    .bipolar-slider__label {
      font-size: 0.875rem;
      font-weight: 600;
      color: var(--color-bp-text);
    }

    .bipolar-slider__inappropriate {
      flex: none;
      display: flex;
      align-items: center;
      gap: var(--spacing-1);
      font-size: 0.75rem;
      font-weight: 500;
      color: var(--color-bp-text-muted);
      white-space: nowrap;
    }

    .bipolar-slider__track {
      display: flex;
      align-items: center;
      gap: var(--spacing-3);
    }

    .bipolar-slider__pole {
      flex: none;
      font-size: 0.75rem;
      color: var(--color-bp-text-muted);
      max-width: 6rem;
    }

    input[type='range'] {
      flex: 1;
      accent-color: var(--color-bp-cobre-500);
      min-height: 44px;
    }
  `,
})
export class BpBipolarSliderComponent {
  readonly id = input.required<string>();
  readonly label = input.required<string>();
  readonly startLabel = input.required<string>();
  readonly endLabel = input.required<string>();
  readonly value = input(50);
  readonly inappropriate = input(false);
  readonly valueChange = output<number>();
  readonly inappropriateChange = output<boolean>();

  protected onInput(event: Event): void {
    this.valueChange.emit(Number((event.target as HTMLInputElement).value));
  }

  protected onInappropriateChange(event: Event): void {
    this.inappropriateChange.emit((event.target as HTMLInputElement).checked);
  }
}
