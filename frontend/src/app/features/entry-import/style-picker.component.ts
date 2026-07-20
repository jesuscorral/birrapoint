import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';

import type { StyleSummary } from './entry-import-api.service';

// Filter-as-you-type over the BJCP catalog (T036) — plain signals, no ReactiveFormsModule/RxJS
// needed for two local input values.
@Component({
  selector: 'app-style-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label>
      Filter styles
      <input
        type="text"
        [value]="filterText()"
        (input)="filterText.set($any($event.target).value)"
      />
    </label>
    <label>
      Style
      <select [value]="selectedCode()" (change)="selectedCode.set($any($event.target).value)">
        <option value="" disabled>Select a style…</option>
        @for (style of filteredStyles(); track style.code) {
          <option [value]="style.code">{{ style.code }} — {{ style.name }}</option>
        }
      </select>
    </label>
    <button type="button" [disabled]="!selectedCode()" (click)="onAssign()">Assign style</button>
  `,
})
export class StylePickerComponent {
  readonly styles = input.required<StyleSummary[]>();
  readonly assign = output<string>();

  protected readonly filterText = signal('');
  protected readonly selectedCode = signal('');

  protected readonly filteredStyles = computed(() => {
    const query = this.filterText().trim().toLowerCase();
    const all = this.styles();
    if (!query) {
      return all;
    }
    return all.filter(
      (style) =>
        style.code.toLowerCase().includes(query) || style.name.toLowerCase().includes(query),
    );
  });

  protected onAssign(): void {
    const code = this.selectedCode();
    if (code) {
      this.assign.emit(code);
    }
  }
}
