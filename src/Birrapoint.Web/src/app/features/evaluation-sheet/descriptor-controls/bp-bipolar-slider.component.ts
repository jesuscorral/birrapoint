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
            [attr.aria-label]="label() + ': inapropiado para el estilo'"
            (change)="onInappropriateChange($event)"
          />
          Inapropiado
        </label>
      </div>
      <div class="bipolar-slider__track">
        <span class="bipolar-slider__pole bipolar-slider__pole--start">{{ startLabel() }}</span>
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
        <span class="bipolar-slider__pole bipolar-slider__pole--end">{{ endLabel() }}</span>
      </div>
    </div>
  `,
  styles: `
    .bipolar-slider {
      container-type: inline-size;
      container-name: bipolar-slider;
      margin: var(--spacing-3) 0;
    }

    .bipolar-slider__header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--spacing-2) var(--spacing-3);
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

    /* Grid, not flex: two pole labels plus a range input never share one row without forcing
       horizontal overflow (a range input's intrinsic min-content width can exceed a flex item's
       default min-width: auto). The row/stacked switch is a container query against .bipolar-
       slider's own inline size — not the viewport — because this component sits in a 2-column
       desktop grid (see evaluation-sheet.component.ts's .descriptor-group): a wide viewport does
       not mean a wide column, and a viewport media query here previously assumed it did, leaving
       ~80px for the track at common tablet widths. Below the threshold the labels sit on their
       own row and the slider takes the full width beneath them; above it, a single inline row
       (capped pole | flexible range | capped pole), so long pole labels can't eat the track. */
    .bipolar-slider__track {
      display: grid;
      grid-template-columns: 1fr 1fr;
      grid-template-areas: 'start end' 'range range';
      gap: var(--spacing-2) var(--spacing-3);
      align-items: center;
    }

    @container bipolar-slider (min-width: 480px) {
      .bipolar-slider__track {
        grid-template-columns: minmax(0, 6rem) minmax(8rem, 1fr) minmax(0, 6rem);
        grid-template-areas: 'start range end';
      }
    }

    .bipolar-slider__pole {
      min-width: 0;
      font-size: 0.75rem;
      color: var(--color-bp-text-muted);
    }

    .bipolar-slider__pole--start {
      grid-area: start;
    }

    .bipolar-slider__pole--end {
      grid-area: end;
      text-align: right;
    }

    input[type='range'] {
      grid-area: range;
      min-width: 0;
      width: 100%;
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
