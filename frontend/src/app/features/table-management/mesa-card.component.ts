import type { CdkDragDrop } from '@angular/cdk/drag-drop';
import { CdkDropList } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { BeerTokenComponent } from './beer-token.component';
import { JudgeSeatComponent } from './judge-seat.component';
import type { TableSummary } from './table-management-api.service';

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
          <div>
            <dt>Mean ABV</dt>
            <dd>{{ meanAbvLabel() }}</dd>
          </div>
          <div>
            <dt>Styles</dt>
            <dd>{{ stylesLabel() }}</dd>
          </div>
          <div>
            <dt>Progress</dt>
            <dd>{{ table().progress.submitted }}/{{ table().progress.total }}</dd>
          </div>
          <div>
            <dt>Judges</dt>
            <dd data-stat="judges">{{ table().judges.length }}</dd>
          </div>
          <div>
            <dt>Beers</dt>
            <dd data-stat="beers">{{ table().samples.length }}</dd>
          </div>
        </dl>
      </header>

      <div class="mesa-body">
        <div class="mesa-column">
          <h4 class="mesa-column__title">Jueces</h4>
          <ul
            class="mesa-seats"
            aria-label="Assigned judges"
            cdkDropList
            [id]="judgeListId()"
            [cdkDropListConnectedTo]="connectedJudgeListIds()"
            (cdkDropListDropped)="judgesDropped.emit($event)"
          >
            @for (judge of table().judges; track judge.id) {
              <li class="roster-row">
                <app-judge-seat [judge]="judge" (activated)="judgeActivated.emit(judge.id)" />
                <span class="roster-row__label">
                  <span class="roster-row__name">{{ judge.displayName }}</span>
                  <span class="roster-row__meta">{{ judge.email }}</span>
                </span>
              </li>
            } @empty {
              <li class="roster-row roster-row--empty">Sin jueces asignados</li>
            }
          </ul>
        </div>

        <div class="mesa-column mesa-column--beers">
          <h4 class="mesa-column__title">Cervezas</h4>
          <ul
            class="mesa-tokens"
            aria-label="Assigned beers"
            cdkDropList
            [id]="beerListId()"
            [cdkDropListConnectedTo]="connectedBeerListIds()"
            (cdkDropListDropped)="beersDropped.emit($event)"
          >
            @for (sample of table().samples; track sample.beerEntryId) {
              <li class="roster-row">
                <app-beer-token
                  [beer]="{
                    id: sample.beerEntryId,
                    blindCode: sample.blindCode,
                    notValidForBos: sample.notValidForBos,
                  }"
                  (activated)="beerActivated.emit(sample.beerEntryId)"
                />
                <span class="roster-row__label">
                  <span class="roster-row__name">{{ sample.styleName }}</span>
                  <span class="roster-row__meta">{{ sample.abvPercent }}% ABV</span>
                </span>
              </li>
            } @empty {
              <li class="roster-row roster-row--empty">Sin cervezas asignadas</li>
            }
          </ul>
        </div>
      </div>
    </article>
  `,
  styles: `
    .mesa-card {
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
      gap: var(--spacing-4);
      margin: var(--spacing-2) 0 0;
    }

    .mesa-stats div {
      display: flex;
      flex-direction: column;
    }

    .mesa-stats dt {
      font-size: 0.75rem;
      font-weight: 600;
      color: var(--color-bp-text-muted);
    }

    .mesa-stats dd {
      margin: 0;
      color: var(--color-bp-text);
      font-weight: 600;
    }

    .mesa-body {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacing-5);
      margin-top: var(--spacing-4);
      padding-top: var(--spacing-4);
      border-top: 1px solid var(--color-bp-border);
    }

    .mesa-column {
      flex: 1 1 220px;
      min-width: 0;
    }

    .mesa-column--beers {
      flex: 2 1 300px;
    }

    .mesa-column__title {
      margin: 0 0 var(--spacing-2);
      font-size: 0.75rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.03em;
      color: var(--color-bp-text-muted);
    }

    .mesa-seats {
      margin: 0;
      padding: 0;
      list-style: none;
      display: flex;
      flex-direction: column;
      gap: var(--spacing-2);
      min-height: 44px;
    }

    .mesa-tokens {
      margin: 0;
      padding: 0;
      list-style: none;
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
      gap: var(--spacing-2);
      align-content: start;
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

  protected readonly stylesLabel = computed(() => {
    const stats = this.table().stats;
    return stats.styleCount === 0 ? '—' : `${stats.styleCount} (${stats.styles.join(', ')})`;
  });
}
