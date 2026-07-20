import { CdkDrag } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

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
    <li
      cdkDrag
      [cdkDragData]="beer().id"
      class="beer-token"
      [class.beer-token--bos-flagged]="beer().notValidForBos"
      [attr.data-entry-id]="beer().id"
      role="button"
      tabindex="0"
      [attr.aria-label]="'Beer ' + beer().blindCode + ' — view details'"
      appClickVsDrag
      (appClickVsDrag)="activated.emit()"
    >
      {{ beer().blindCode }}
    </li>
  `,
  styles: `
    .beer-token {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 64px;
      height: 64px;
      min-width: 64px;
      min-height: 64px;
      border-radius: 0.5rem;
      background: #d97706;
      color: #fff;
      font-size: 0.8rem;
      font-weight: 700;
      cursor: grab;
      user-select: none;
      list-style: none;
      text-align: center;
      padding: 0.25rem;
    }

    .beer-token--bos-flagged {
      box-shadow: 0 0 0 2px #dc2626 inset;
    }

    .beer-token:focus-visible {
      outline: 2px solid #d97706;
      outline-offset: 2px;
    }
  `,
})
export class BeerTokenComponent {
  readonly beer = input.required<BeerTokenData>();
  readonly activated = output<void>();
}
