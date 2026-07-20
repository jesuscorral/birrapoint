import type { CdkDragDrop } from '@angular/cdk/drag-drop';
import { CdkDropList } from '@angular/cdk/drag-drop';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { BeerTokenComponent } from './beer-token.component';
import { JudgeSeatComponent } from './judge-seat.component';
import type { TableSummary } from './table-management-api.service';

interface SeatPosition {
  left: number;
  top: number;
}

// Evenly distributes `count` seats around an ellipse centered in the board, starting at the top
// and going clockwise — the physical-table metaphor from the "Crear mesas" prototype.
function seatPosition(index: number, count: number): SeatPosition {
  if (count === 0) {
    return { left: 50, top: 50 };
  }
  const angle = (2 * Math.PI * index) / count - Math.PI / 2;
  return { left: 50 + 42 * Math.cos(angle), top: 50 + 42 * Math.sin(angle) };
}

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
        </dl>
      </header>

      <div class="mesa-board">
        <ul
          class="mesa-seats"
          aria-label="Assigned judges"
          cdkDropList
          [id]="judgeListId()"
          [cdkDropListConnectedTo]="connectedJudgeListIds()"
          (cdkDropListDropped)="judgesDropped.emit($event)"
        >
          @for (judge of table().judges; track judge.id; let i = $index) {
            <app-judge-seat
              class="mesa-seat"
              [judge]="judge"
              [style.left.%]="seatPositionOf(i).left"
              [style.top.%]="seatPositionOf(i).top"
              (activated)="judgeActivated.emit(judge.id)"
            />
          }
        </ul>

        <ul
          class="mesa-tokens"
          aria-label="Assigned beers"
          cdkDropList
          [id]="beerListId()"
          [cdkDropListConnectedTo]="connectedBeerListIds()"
          (cdkDropListDropped)="beersDropped.emit($event)"
        >
          @for (sample of table().samples; track sample.beerEntryId) {
            <app-beer-token
              [beer]="{
                id: sample.beerEntryId,
                blindCode: sample.blindCode,
                notValidForBos: sample.notValidForBos,
              }"
              (activated)="beerActivated.emit(sample.beerEntryId)"
            />
          }
        </ul>
      </div>
    </article>
  `,
  styles: `
    .mesa-card {
      border: 1px solid #d1d5db;
      border-radius: 0.75rem;
      padding: 1rem;
    }

    .mesa-stats {
      display: flex;
      gap: 1rem;
      margin: 0.5rem 0 0;
    }

    .mesa-stats div {
      display: flex;
      flex-direction: column;
    }

    .mesa-board {
      position: relative;
      margin-top: 1.5rem;
      aspect-ratio: 1.4 / 1;
      border-radius: 999px;
      background: #ecfccb;
      border: 2px solid #65a30d;
    }

    .mesa-seats {
      position: absolute;
      inset: 0;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .mesa-seat {
      position: absolute;
      transform: translate(-50%, -50%);
    }

    .mesa-tokens {
      position: absolute;
      inset: 15%;
      margin: 0;
      padding: 0;
      list-style: none;
      display: flex;
      flex-wrap: wrap;
      align-content: center;
      justify-content: center;
      gap: 0.5rem;
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

  protected seatPositionOf(index: number): SeatPosition {
    return seatPosition(index, this.table().judges.length);
  }
}
