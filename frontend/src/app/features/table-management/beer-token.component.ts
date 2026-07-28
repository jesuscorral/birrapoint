import { CdkDrag } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { ClickVsDragDirective } from './click-vs-drag.directive';

// Minimal shape both TableSample (seated) and EntryListItem (unassigned) satisfy.
export interface BeerTokenData {
  id: string;
  blindCode: string;
  notValidForBos: boolean;
}

// T048A/T048B/T048C: a beer draggable used both seated on a MesaCard and in the "Unassigned"
// column — one shared implementation so the click-vs-drag disambiguation and ~64px target sizing
// live in exactly one place.
@Component({
  selector: 'app-beer-token',
  standalone: true,
  imports: [CdkDrag, ClickVsDragDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      cdkDrag
      [cdkDragData]="beer().id"
      class="beer-token"
      [class.beer-token--bos-flagged]="beer().notValidForBos"
      [attr.data-entry-id]="beer().id"
      role="button"
      tabindex="0"
      [attr.aria-label]="'Beer ' + beer().blindCode + ' — view details'"
      [attr.aria-describedby]="beer().notValidForBos ? bosNoteId() : null"
      appClickVsDrag
      (appClickVsDrag)="activated.emit()"
    >
      {{ beer().blindCode }}
      @if (beer().notValidForBos) {
        <span class="bos-marker" aria-hidden="true">&#9888;</span>
      }
    </div>
    @if (beer().notValidForBos) {
      <span [id]="bosNoteId()" class="sr-only">Not valid for Best of Show</span>
    }
  `,
  styles: `
    :host {
      display: contents;
    }

    .beer-token {
      position: relative;
      display: flex;
      align-items: center;
      justify-content: center;
      width: 64px;
      height: 64px;
      min-width: 64px;
      min-height: 64px;
      border-radius: 0.5rem;
      background: #92400e;
      color: #fff;
      font-size: 0.8rem;
      font-weight: 700;
      cursor: grab;
      user-select: none;
      text-align: center;
      padding: 0.25rem;
    }

    .beer-token--bos-flagged {
      /* #fbbf24 (amber-400) against this token's #92400e background is ~4.24:1 -- the previous
         #dc2626 ring was ~1.47:1, well under WCAG 1.4.11's 3:1 non-text contrast minimum. */
      box-shadow: 0 0 0 2px #fbbf24 inset;
    }

    .beer-token:focus-visible {
      outline: 2px solid #92400e;
      outline-offset: 2px;
    }

    .bos-marker {
      position: absolute;
      top: -0.35rem;
      right: -0.35rem;
      color: #fbbf24;
      background: #1f2937;
      border-radius: 9999px;
      width: 1rem;
      height: 1rem;
      line-height: 1rem;
      font-size: 0.7rem;
      text-align: center;
    }

    .sr-only {
      position: absolute;
      width: 1px;
      height: 1px;
      padding: 0;
      margin: -1px;
      overflow: hidden;
      clip: rect(0, 0, 0, 0);
      white-space: nowrap;
      border: 0;
    }
  `,
})
export class BeerTokenComponent {
  readonly beer = input.required<BeerTokenData>();
  readonly activated = output<void>();

  // WCAG 1.4.1 (Use of Color): the BOS-flagged state must not be conveyed by the ring color alone
  // (aria-describedby here, plus the visible aria-hidden marker glyph in the template) — the base
  // aria-label deliberately stays exactly "Beer {code} — view details" regardless of flag state,
  // since several E2E specs (us5/us6/us9-tables/-order/-dashboard) locate a flagged beer by that
  // exact accessible name; describedby adds information without changing the name they match on.
  protected readonly bosNoteId = computed(() => `bos-note-${this.beer().id}`);
}
