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
//
// T124: initials alone were not enough to tell two judges apart while assigning them to tables, so
// the avatar now carries the full display name underneath it. The initials stay as the visual
// anchor (and as the fallback when `showName` is off).
@Component({
  selector: 'app-judge-seat',
  standalone: true,
  imports: [CdkDrag, ClickVsDragDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      cdkDrag
      [cdkDragData]="judge().id"
      class="judge-seat"
      [class.judge-seat--named]="showName()"
      [class.judge-seat--dense]="dense()"
      [attr.data-judge-id]="judge().id"
      role="button"
      tabindex="0"
      [attr.aria-label]="'Judge ' + judge().displayName + ' — view details'"
      appClickVsDrag
      (appClickVsDrag)="activated.emit()"
    >
      <span class="judge-seat__avatar" aria-hidden="true">{{ initials() }}</span>
      @if (showName()) {
        <!-- aria-hidden: the accessible name above already announces the full display name, so
             exposing it twice would make every seat read as a duplicated label. -->
        <span class="judge-seat__name" aria-hidden="true">{{ judge().displayName }}</span>
      }
    </div>
  `,
  styles: `
    :host {
      display: contents;
    }

    .judge-seat {
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: grab;
      user-select: none;
      border-radius: var(--radius-full);
    }

    .judge-seat__avatar {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 38px;
      height: 38px;
      min-width: 38px;
      min-height: 38px;
      border-radius: var(--radius-full);
      /* White text on --color-bp-verde-600 (#2e6b57) is ~6.25:1 -- comfortably passes WCAG AA's
         4.5:1 for this small (0.7rem) bold label. */
      background: var(--color-bp-verde-600);
      color: #fff;
      font-size: 0.7rem;
      font-weight: 600;
    }

    /* Named form: avatar on top, full name underneath, the whole stack one drag target. */
    .judge-seat--named {
      flex-direction: column;
      gap: var(--spacing-1);
      width: 5.5rem;
      padding: var(--spacing-1);
      border-radius: var(--radius-md);
      text-align: center;
    }

    .judge-seat__name {
      font-size: 0.6875rem;
      font-weight: 600;
      line-height: 1.15;
      /* --color-bp-text-muted (#5b655f) on the card surface is ~6.1:1 -- passes AA at this size. */
      color: var(--color-bp-text-muted);
      /* Two lines then ellipsis: "María de los Ángeles Fernández" must not push the seat grid out
         of alignment, but one-word truncation ("María…") would defeat the point of showing it. */
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
      overflow-wrap: anywhere;
    }

    .judge-seat--dense {
      flex-direction: row;
      width: auto;
      max-width: 100%;
      gap: var(--spacing-1);
      padding: 0 var(--spacing-1) 0 0;
    }

    .judge-seat--dense .judge-seat__avatar {
      width: 28px;
      height: 28px;
      min-width: 28px;
      min-height: 28px;
      font-size: 0.625rem;
    }

    .judge-seat--dense .judge-seat__name {
      font-size: 0.6875rem;
      -webkit-line-clamp: 1;
      max-width: 6rem;
      text-align: left;
    }

    .judge-seat:focus-visible {
      outline: 2px solid var(--color-bp-verde-600);
      outline-offset: 2px;
    }
  `,
})
export class JudgeSeatComponent {
  readonly judge = input.required<TableJudge>();
  // Defaults to the named form — every current call site wants it (T124). Kept as an input rather
  // than hardcoded so a future dense view can fall back to the bare avatar without a fork.
  readonly showName = input(true);
  // T125b: seated inside a rail card the avatar-over-name stack is what made those cards tall, so
  // the dense form lays the two out in a row with a one-line name. The name stays visible — losing
  // it was the whole complaint that put it there.
  readonly dense = input(false);
  readonly activated = output<void>();

  protected readonly initials = computed(() => initialsOf(this.judge().displayName));
}
