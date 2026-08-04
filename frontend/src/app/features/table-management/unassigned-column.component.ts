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
          <li>
            <app-judge-seat [judge]="judge" (activated)="judgeActivated.emit(judge.id)" />
          </li>
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
          <li>
            <app-beer-token
              [beer]="{
                id: beer.id,
                blindCode: beer.blindCode,
                notValidForBos: beer.notValidForBos,
              }"
              (activated)="beerActivated.emit(beer.id)"
            />
          </li>
        }
      </ul>
    </section>
  `,
  styles: `
    .unassigned-column {
      border: 1px dashed var(--color-bp-border-strong);
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
      flex-wrap: wrap;
      gap: var(--spacing-2);
      margin: 0 0 var(--spacing-4);
      padding: 0;
      list-style: none;
      min-height: 64px;
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
