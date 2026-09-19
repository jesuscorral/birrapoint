import type { CdkDragDrop } from '@angular/cdk/drag-drop';
import { CdkDropList } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import type { EntryListItem } from '../../core/api/entries-api.service';
import { BeerTokenComponent } from './beer-token.component';
import { UNCATEGORIZED_COLOR } from './category-color';
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

      <h3>Cervezas sin asignar ({{ beersCountLabel() }})</h3>
      <!-- Filtering the pool is a status message, not a navigation: the heading text changes and
           the list re-renders with no focus move, so a screen-reader user gets nothing without a
           live region (WCAG 4.1.3). The heading itself cannot be one -- announcing on every
           assignment would be noise -- so this mirrors just the filtered count.
           aria-live rather than role="status": the board deliberately exposes exactly one
           status-role region (the BOS banner), which an E2E spec addresses by role. -->
      <p class="sr-only" aria-live="polite">{{ filterStatusLabel() }}</p>
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
                styleCode: beer.styleCode,
                notValidForBos: beer.notValidForBos,
                styleName: beer.styleName,
                abvPercent: beer.abvPercent,
                competitionCategoryName: beer.competitionCategoryName,
                bjcpCategoryNumber: beer.bjcpCategoryNumber,
                bjcpCategoryName: beer.bjcpCategoryName,
                categoryColor: categoryColorFor(beer.competitionCategoryName),
              }"
              (activated)="beerActivated.emit(beer.id)"
            />
          </li>
        }
        @if (beers().length === 0) {
          <!-- aria-hidden only while a filter is the reason the list is empty: filterStatusLabel's
               live region already speaks that exact sentence, so leaving this in the a11y tree too
               would say it twice. When the pool is genuinely empty (no filter active) this is the
               only copy that exists -- filterStatusLabel stays silent then -- so it must stay
               reachable. -->
          <li class="unassigned-empty" [attr.aria-hidden]="beersTotal() > 0 ? true : null">
            {{ emptyBeersLabel() }}
          </li>
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
    }

    .unassigned-column h3 {
      font-family: 'Fraunces', serif;
      font-size: 0.9375rem;
      font-weight: 600;
      color: var(--color-bp-text);
      margin: 0 0 var(--spacing-2);
    }

    .unassigned-list {
      margin: 0 0 var(--spacing-4);
      padding: 0;
      list-style: none;
      min-height: 64px;
    }

    .unassigned-list--judges {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacing-2);
      /* Judges are few and short; cap them so a large roster cannot push the beers — the thing
         actually being distributed — out of view. */
      max-height: 12rem;
      overflow-y: auto;
      overscroll-behavior: contain;
    }

    /* T125: the pool is the full width of the board now, not a sidebar, so beers lay out as a
       grid. T125b caps its height and scrolls it internally: the pool sits above the tables rail
       now, and an uncapped pool would push the rail off screen exactly when the organizer has the
       most beers left to place. */
    .unassigned-list--beers {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(13.5rem, 1fr));
      gap: var(--spacing-2);
      align-content: start;
      /* Capped in rem as well as vh: a pure vh cap does not grow with text size, so at 200% zoom
         the pool collapsed to about one row (WCAG 1.4.4). Every sibling cap here is rem-based. */
      max-height: max(40vh, 18rem);
      overflow-y: auto;
      overscroll-behavior: contain;
      /* Room for the focus ring on the last row, which the scroll container would otherwise clip. */
      padding: 2px;
    }

    .unassigned-empty {
      grid-column: 1 / -1;
      color: var(--color-bp-text-muted);
      font-size: 0.8125rem;
      align-self: center;
    }
  `,
})
export class UnassignedColumnComponent {
  readonly judges = input.required<JudgeListItem[]>();
  readonly beers = input.required<EntryListItem[]>();
  // The unfiltered size of the pool, so the heading can distinguish "3 left to place" from
  // "3 match the current filter" (T125's toolbar). Equal to beers().length when no filter is
  // applied — the board always knows the total, so there is no "unknown" case to model.
  readonly beersTotal = input.required<number>();
  readonly connectedJudgeListIds = input.required<string[]>();
  readonly connectedBeerListIds = input.required<string[]>();
  // T125c: computed once by the board over every entry in the competition (assigned or not), so a
  // category's color stays identical whether its beers are seated on a table or still in this pool.
  readonly categoryColorMap = input.required<ReadonlyMap<string, string>>();

  readonly judgeActivated = output<string>();
  readonly beerActivated = output<string>();
  readonly judgesDropped = output<CdkDragDrop<unknown>>();
  readonly beersDropped = output<CdkDragDrop<unknown>>();

  protected readonly judgesListId = UNASSIGNED_JUDGES_LIST_ID;
  protected readonly beersListId = UNASSIGNED_BEERS_LIST_ID;

  protected readonly beersCountLabel = computed(() => {
    const shown = this.beers().length;
    const total = this.beersTotal();
    return total === shown ? `${shown}` : `${shown} de ${total}`;
  });

  // Empty while unfiltered, so the live region stays silent during ordinary drag-and-drop and
  // speaks only when a filter is actually narrowing the pool.
  protected readonly filterStatusLabel = computed(() => {
    const total = this.beersTotal();
    const shown = this.beers().length;
    if (total === shown) {
      return '';
    }
    return shown === 0
      ? 'Ninguna cerveza coincide con el filtro'
      : `${shown} de ${total} cervezas coinciden con el filtro`;
  });

  protected readonly emptyBeersLabel = computed(() =>
    this.beersTotal() > 0
      ? 'Ninguna cerveza coincide con el filtro'
      : 'Todas las cervezas están asignadas',
  );

  protected categoryColorFor(categoryName: string | null): string {
    return categoryName
      ? (this.categoryColorMap().get(categoryName) ?? UNCATEGORIZED_COLOR)
      : UNCATEGORIZED_COLOR;
  }
}
