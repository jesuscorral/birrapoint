import { CdkDrag } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { ClickVsDragDirective } from './click-vs-drag.directive';
import type { TableJudge } from './table-management-api.service';

function initialsOf(displayName: string): string {
  return displayName
    .trim()
    .split(/\s+/)
    .filter((part) => part.length > 0)
    .slice(0, 2)
    .map((part) => part[0]!.toUpperCase())
    .join('');
}

// T048A/T048B/T048C: a judge draggable used both seated on a MesaCard and in the "Unassigned"
// column — one shared implementation so the click-vs-drag disambiguation and ~38px target sizing
// live in exactly one place.
@Component({
  selector: 'app-judge-seat',
  standalone: true,
  imports: [CdkDrag, ClickVsDragDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <li
      cdkDrag
      [cdkDragData]="judge().id"
      class="judge-seat"
      [attr.data-judge-id]="judge().id"
      role="button"
      tabindex="0"
      [attr.aria-label]="'Judge ' + judge().displayName + ' — view details'"
      appClickVsDrag
      (appClickVsDrag)="activated.emit()"
    >
      {{ initials() }}
    </li>
  `,
  styles: `
    .judge-seat {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 38px;
      height: 38px;
      min-width: 38px;
      min-height: 38px;
      border-radius: 9999px;
      background: #1d4ed8;
      color: #fff;
      font-size: 0.7rem;
      font-weight: 600;
      cursor: grab;
      user-select: none;
      list-style: none;
    }

    .judge-seat:focus-visible {
      outline: 2px solid #1d4ed8;
      outline-offset: 2px;
    }
  `,
})
export class JudgeSeatComponent {
  readonly judge = input.required<TableJudge>();
  readonly activated = output<void>();

  protected readonly initials = computed(() => initialsOf(this.judge().displayName));
}
