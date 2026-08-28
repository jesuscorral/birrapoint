import type { CdkDragDrop } from '@angular/cdk/drag-drop';
import { CdkDropList } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import type { EntryListItem } from '../../core/api/entries-api.service';
import { BeerTokenComponent } from './beer-token.component';
import { JudgeSeatComponent } from './judge-seat.component';
import type { JudgeListItem } from './table-management-api.service';

// T048's "Unassigned" source column — deliberately a plain list (no table/seat iconography), the
// entry point for dragging a judge/beer onto a MesaCard, and (T048A) an equally valid place to
// click-open a beer/judge detail.
export const UNASSIGNED_JUDGES_LIST_ID = 'judges-unassigned';
export const UNASSIGNED_BEERS_LIST_ID = 'beers-unassigned';

@Component({
  selector: 'app-unassigned-column',
  standalone: true,
  imports: [CdkDropList, JudgeSeatComponent, BeerTokenComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="unassigned-column" aria-label="Unassigned">
      <h3>Jueces sin asignar ({{ judges().length }})</h3>
      <ul
        class="unassigned-list"
        cdkDropList
        [id]="judgesListId"
        [cdkDropListConnectedTo]="connectedJudgeListIds()"
        (cdkDropListDropped)="judgesDropped.emit($event)"
      >
        @for (judge of judges(); track judge.id) {
          <li class="roster-row">
            <app-judge-seat [judge]="judge" (activated)="judgeActivated.emit(judge.id)" />
            <span class="roster-row__label">
              <span class="roster-row__name">{{ judge.displayName }}</span>
              <span class="roster-row__meta">{{ judge.email }}</span>
            </span>
          </li>
        } @empty {
          <li class="roster-row roster-row--empty">Todos los jueces están asignados</li>
        }
      </ul>

      <h3>Cervezas sin asignar ({{ beers().length }})</h3>
      <ul
        class="unassigned-list"
        cdkDropList
        [id]="beersListId"
        [cdkDropListConnectedTo]="connectedBeerListIds()"
        (cdkDropListDropped)="beersDropped.emit($event)"
      >
        @for (beer of beers(); track beer.id) {
          <li class="roster-row">
            <app-beer-token
              [beer]="{
                id: beer.id,
                blindCode: beer.blindCode,
                notValidForBos: beer.notValidForBos,
              }"
              (activated)="beerActivated.emit(beer.id)"
            />
            <span class="roster-row__label">
              <span class="roster-row__name">{{ beer.styleName }}</span>
              <span class="roster-row__meta">{{ beer.abvPercent }}% ABV</span>
            </span>
          </li>
        } @empty {
          <li class="roster-row roster-row--empty">Todas las cervezas están asignadas</li>
        }
      </ul>
    </section>
  `,
  styles: `
    :host {
      display: block;
      position: sticky;
      top: var(--spacing-4);
      align-self: start;
      max-height: calc(100vh - var(--spacing-12));
      overflow-y: auto;
    }

    .unassigned-column {
      /* --color-bp-border-strong only gives ~1.83:1 against the surface here -- too low for a
         border that's the sole visual affordance of a drag target. --color-bp-text-muted restores
         it to ~2.55:1 (matching the original #9ca3af) without reintroducing a hardcoded hex. */
      border: 1px dashed var(--color-bp-text-muted);
      border-radius: var(--radius-lg);
      padding: var(--spacing-4);
      background: var(--color-bp-surface);
    }

    .unassigned-column h3 {
      font-family: 'Fraunces', serif;
      font-size: 0.9375rem;
      font-weight: 600;
      color: var(--color-bp-text);
      margin: 0 0 var(--spacing-2);
    }

    .unassigned-list {
      display: flex;
      flex-direction: column;
      gap: var(--spacing-2);
      margin: 0 0 var(--spacing-4);
      padding: 0;
      list-style: none;
      min-height: 44px;
    }

    .roster-row {
      display: flex;
      align-items: center;
      gap: var(--spacing-2);
      padding: var(--spacing-2);
      border-radius: var(--radius-md);
      background: var(--color-bp-hueso-50);
      min-width: 0;
    }

    .roster-row--empty {
      color: var(--color-bp-text-subtle);
      font-size: 0.8125rem;
      font-style: italic;
      background: transparent;
      padding: var(--spacing-2) 0;
    }

    .roster-row__label {
      display: flex;
      flex-direction: column;
      min-width: 0;
    }

    .roster-row__name {
      font-size: 0.8125rem;
      font-weight: 600;
      color: var(--color-bp-text);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .roster-row__meta {
      font-size: 0.75rem;
      color: var(--color-bp-text-muted);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  `,
})
export class UnassignedColumnComponent {
  readonly judges = input.required<JudgeListItem[]>();
  readonly beers = input.required<EntryListItem[]>();
  readonly connectedJudgeListIds = input.required<string[]>();
  readonly connectedBeerListIds = input.required<string[]>();

  readonly judgeActivated = output<string>();
  readonly beerActivated = output<string>();
  readonly judgesDropped = output<CdkDragDrop<unknown>>();
  readonly beersDropped = output<CdkDragDrop<unknown>>();

  protected readonly judgesListId = UNASSIGNED_JUDGES_LIST_ID;
  protected readonly beersListId = UNASSIGNED_BEERS_LIST_ID;
}
