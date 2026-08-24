import type { CdkDragDrop } from '@angular/cdk/drag-drop';
import { CdkDropList } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { BeerTokenComponent } from './beer-token.component';
import { JudgeSeatComponent } from './judge-seat.component';
import type { TableSummary } from './table-management-api.service';

// T124: the oval "physical table" board was replaced by a compact card. Seats arranged around an
// ellipse cannot carry a judge's full name without colliding, and the ellipse's fixed 1.4:1 aspect
// ratio forced every card tall enough that only two or three tables fitted on screen at once —
// both directly against what this screen is for (see the board's grid in table-board.component).
// What the organizer needs while assigning is the balance read-out (how many beers, mean ABV,
// which styles) and a drop target that is always visible, not the seating metaphor.
@Component({
  selector: 'app-mesa-card',
  standalone: true,
  imports: [CdkDropList, JudgeSeatComponent, BeerTokenComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="mesa-card" [attr.data-table-id]="table().id">
      <header class="mesa-header">
        <h3>{{ table().name }}</h3>

        <dl class="mesa-stats" aria-label="Table stats">
          <div class="mesa-stats__item mesa-stats__item--primary">
            <dt>Cervezas</dt>
            <dd data-stat="beers">{{ table().samples.length }}</dd>
          </div>
          <div class="mesa-stats__item mesa-stats__item--primary">
            <dt>Media ABV</dt>
            <dd>{{ meanAbvLabel() }}</dd>
          </div>
          <div class="mesa-stats__item">
            <dt>Estilos</dt>
            <dd>{{ styleCountLabel() }}</dd>
          </div>
          <div class="mesa-stats__item">
            <dt>Jueces</dt>
            <dd data-stat="judges">{{ table().judges.length }}</dd>
          </div>
          <div class="mesa-stats__item">
            <dt>Progreso</dt>
            <dd>{{ table().progress.submitted }}/{{ table().progress.total }}</dd>
          </div>
        </dl>

        <p class="mesa-styles" [attr.title]="stylesLabel()">{{ stylesLabel() }}</p>
      </header>

      <div class="mesa-zone">
        <p class="mesa-zone__label">Jueces</p>
        <ul
          class="mesa-seats"
          aria-label="Assigned judges"
          cdkDropList
          [id]="judgeListId()"
          [cdkDropListConnectedTo]="connectedJudgeListIds()"
          (cdkDropListDropped)="judgesDropped.emit($event)"
        >
          @for (judge of table().judges; track judge.id) {
            <li>
              <app-judge-seat [judge]="judge" (activated)="judgeActivated.emit(judge.id)" />
            </li>
          }
          @if (table().judges.length === 0) {
            <li class="mesa-empty" aria-hidden="true">Arrastra jueces aquí</li>
          }
        </ul>
      </div>

      <div class="mesa-zone">
        <p class="mesa-zone__label">Cervezas</p>
        <ul
          class="mesa-tokens"
          aria-label="Assigned beers"
          cdkDropList
          [id]="beerListId()"
          [cdkDropListConnectedTo]="connectedBeerListIds()"
          (cdkDropListDropped)="beersDropped.emit($event)"
        >
          @for (sample of table().samples; track sample.beerEntryId) {
            <li>
              <app-beer-token
                variant="mini"
                [beer]="{
                  id: sample.beerEntryId,
                  blindCode: sample.blindCode,
                  notValidForBos: sample.notValidForBos,
                  styleName: sample.styleName,
                  abvPercent: sample.abvPercent,
                  competitionCategoryName: sample.competitionCategoryName,
                  bjcpCategoryNumber: sample.bjcpCategoryNumber,
                  bjcpCategoryName: sample.bjcpCategoryName,
                }"
                (activated)="beerActivated.emit(sample.beerEntryId)"
              />
            </li>
          }
          @if (table().samples.length === 0) {
            <li class="mesa-empty" aria-hidden="true">Arrastra cervezas aquí</li>
          }
        </ul>
      </div>
    </article>
  `,
  styles: `
    .mesa-card {
      display: flex;
      flex-direction: column;
      gap: var(--spacing-3);
      border: 1px solid var(--color-bp-border);
      border-radius: var(--radius-lg);
      padding: var(--spacing-4);
      background: var(--color-bp-surface);
    }

    .mesa-header h3 {
      font-family: 'Fraunces', serif;
      font-size: 1.0625rem;
      font-weight: 600;
      color: var(--color-bp-text);
      margin: 0;
    }

    .mesa-stats {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacing-2) var(--spacing-4);
      margin: var(--spacing-2) 0 0;
    }

    .mesa-stats__item {
      display: flex;
      flex-direction: column;
    }

    .mesa-stats dt {
      font-size: 0.6875rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--color-bp-text-muted);
    }

    .mesa-stats dd {
      margin: 0;
      color: var(--color-bp-text);
      font-weight: 600;
      font-size: 0.875rem;
    }

    /* "How many beers" and "mean ABV" are what the organizer balances a table on, so they read
       first; the rest are supporting counts. */
    .mesa-stats__item--primary dd {
      font-family: 'Fraunces', serif;
      font-size: 1.375rem;
      line-height: 1.1;
      color: var(--color-bp-cobre-700);
    }

    .mesa-styles {
      margin: var(--spacing-2) 0 0;
      font-size: 0.75rem;
      line-height: 1.35;
      color: var(--color-bp-text-muted);
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    .mesa-zone__label {
      margin: 0 0 var(--spacing-1);
      font-size: 0.6875rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--color-bp-text-subtle);
    }

    .mesa-seats,
    .mesa-tokens {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacing-2);
      margin: 0;
      padding: var(--spacing-2);
      list-style: none;
      border-radius: var(--radius-md);
      /* The drop target has to be visible as a target even while empty (it is the whole point of
         the screen), so it carries its own tinted surface rather than a hairline border. */
      background: var(--color-bp-verde-50);
      border: 1px dashed var(--color-bp-verde-400);
      min-height: 48px;
      align-content: flex-start;
    }

    .mesa-tokens {
      background: var(--color-bp-cobre-50);
      border-color: var(--color-bp-cobre-300);
    }

    .mesa-empty {
      /* --color-bp-text-muted on the tinted zone surfaces is >4.5:1; text-subtle would not be. */
      color: var(--color-bp-text-muted);
      font-size: 0.75rem;
      font-weight: 500;
      align-self: center;
      padding: var(--spacing-1) var(--spacing-2);
    }
  `,
})
export class MesaCardComponent {
  readonly table = input.required<TableSummary>();
  readonly connectedJudgeListIds = input.required<string[]>();
  readonly connectedBeerListIds = input.required<string[]>();

  readonly judgeActivated = output<string>();
  readonly beerActivated = output<string>();
  readonly judgesDropped = output<CdkDragDrop<unknown>>();
  readonly beersDropped = output<CdkDragDrop<unknown>>();

  protected readonly judgeListId = computed(() => `judges-${this.table().id}`);
  protected readonly beerListId = computed(() => `beers-${this.table().id}`);

  protected readonly meanAbvLabel = computed(() => {
    const meanAbv = this.table().stats.meanAbv;
    return meanAbv === null ? '—' : `${meanAbv.toFixed(1)}%`;
  });

  protected readonly styleCountLabel = computed(() => {
    const { styleCount } = this.table().stats;
    return styleCount === 0 ? '—' : `${styleCount}`;
  });

  // The names themselves, on their own line — the count already has its own stat slot above, so
  // repeating it here ("3 (A, B, C)") only ate the width the names needed.
  protected readonly stylesLabel = computed(() => {
    const { styles } = this.table().stats;
    return styles.length === 0 ? '—' : styles.join(' · ');
  });
}
