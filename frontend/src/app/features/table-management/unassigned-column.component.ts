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
//
// T124: this is now a fixed side panel rather than one more wrapping card in the board flow, and
// its beers render in the `full` token variant — style, competition category and real ABV are what
// the organizer picks the next beer to place by, and this is the only place with the width to show
// them. It scrolls independently so the tables grid beside it stays put while dragging.
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
        class="unassigned-list unassigned-list--judges"
        cdkDropList
        [id]="judgesListId"
        [cdkDropListConnectedTo]="connectedJudgeListIds()"
        (cdkDropListDropped)="judgesDropped.emit($event)"
      >
        @for (judge of judges(); track judge.id) {
          <li>
            <app-judge-seat [judge]="judge" (activated)="judgeActivated.emit(judge.id)" />
          </li>
        }
        @if (judges().length === 0) {
          <li class="unassigned-empty" aria-hidden="true">Todos los jueces están asignados</li>
        }
      </ul>

      <h3>Cervezas sin asignar ({{ beers().length }})</h3>
      <ul
        class="unassigned-list unassigned-list--beers"
        cdkDropList
        [id]="beersListId"
        [cdkDropListConnectedTo]="connectedBeerListIds()"
        (cdkDropListDropped)="beersDropped.emit($event)"
      >
        @for (beer of beers(); track beer.id) {
          <li>
            <app-beer-token
              variant="full"
              [beer]="{
                id: beer.id,
                blindCode: beer.blindCode,
                notValidForBos: beer.notValidForBos,
                styleName: beer.styleName,
                abvPercent: beer.abvPercent,
                competitionCategoryName: beer.competitionCategoryName,
                bjcpCategoryNumber: beer.bjcpCategoryNumber,
                bjcpCategoryName: beer.bjcpCategoryName,
              }"
              (activated)="beerActivated.emit(beer.id)"
            />
          </li>
        }
        @if (beers().length === 0) {
          <li class="unassigned-empty" aria-hidden="true">Todas las cervezas están asignadas</li>
        }
      </ul>
    </section>
  `,
  styles: `
    .unassigned-column {
      /* --color-bp-border-strong only gives ~1.83:1 against the surface here -- too low for a
         border that's the sole visual affordance of a drag target. --color-bp-text-muted restores
         it to ~2.55:1 (matching the original #9ca3af) without reintroducing a hardcoded hex. */
      border: 1px dashed var(--color-bp-text-muted);
      border-radius: var(--radius-lg);
      padding: var(--spacing-4);
      background: var(--color-bp-surface);
      /* Sticks alongside the tables grid so the source list stays reachable while the organizer
         scrolls through tables; capped so a 200-entry competition scrolls inside the panel rather
         than pushing the grid off screen. */
      position: sticky;
      top: var(--spacing-4);
      max-height: calc(100vh - var(--spacing-12));
      overflow-y: auto;
      overscroll-behavior: contain;
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
      gap: var(--spacing-2);
      margin: 0 0 var(--spacing-4);
      padding: 0;
      list-style: none;
      min-height: 64px;
    }

    .unassigned-list--judges {
      flex-wrap: wrap;
    }

    /* One card per row: the detailed beer token is a horizontal card, so wrapping them side by side
       would truncate the style name this variant exists to show. */
    .unassigned-list--beers {
      flex-direction: column;
    }

    .unassigned-empty {
      color: var(--color-bp-text-muted);
      font-size: 0.8125rem;
      align-self: center;
    }

    @media (max-width: 900px) {
      .unassigned-column {
        position: static;
        max-height: none;
        overflow-y: visible;
      }
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
