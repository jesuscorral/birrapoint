import { CdkTrapFocus } from '@angular/cdk/a11y';
import type { OnInit } from '@angular/core';
import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';

export interface BeerDetailContent {
  kind: 'beer';
  id: string;
  blindCode: string;
  styleName: string;
  abvLow: number | null;
  abvHigh: number | null;
}

export interface JudgeDetailContent {
  kind: 'judge';
  id: string;
  displayName: string;
  email: string;
}

export type DetailModalContent = BeerDetailContent | JudgeDetailContent;

export interface TableOption {
  id: string;
  name: string;
}

// T048A (click-to-detail) + the keyboard-accessible equivalent of T048B's drag-and-drop
// reassignment: the "Move to" control lets a keyboard-only user reassign a judge/beer to a
// different table (or back to Unassigned) without ever touching the CDK drag gesture.
@Component({
  selector: 'app-table-detail-modal',
  standalone: true,
  imports: [CdkTrapFocus],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="modal-backdrop" role="presentation" (click)="closed.emit()">
      <div
        class="modal-panel"
        role="dialog"
        aria-modal="true"
        [attr.aria-label]="title()"
        cdkTrapFocus
        cdkTrapFocusAutoCapture
        (click)="$event.stopPropagation()"
        (keydown.escape)="closed.emit()"
      >
        <h2>{{ title() }}</h2>

        @if (beerContent(); as beer) {
          <dl>
            <div>
              <dt>Blind code</dt>
              <dd>{{ beer.blindCode }}</dd>
            </div>
            <div>
              <dt>Style</dt>
              <dd>{{ beer.styleName }}</dd>
            </div>
            @if (abvLabel(); as abv) {
              <div>
                <dt>ABV</dt>
                <dd>{{ abv }}</dd>
              </div>
            }
          </dl>
        } @else if (judgeContent(); as judge) {
          <dl>
            <div>
              <dt>Name</dt>
              <dd>{{ judge.displayName }}</dd>
            </div>
            <div>
              <dt>Email</dt>
              <dd>{{ judge.email }}</dd>
            </div>
          </dl>
        }

        <p>Assigned table: {{ assignedTableNames() }}</p>

        <label>
          Move to
          <select [value]="moveTarget()" (change)="moveTarget.set($any($event.target).value)">
            <option value="">Unassigned</option>
            @for (table of tables(); track table.id) {
              <option [value]="table.id">{{ table.name }}</option>
            }
          </select>
        </label>
        <button type="button" (click)="onMove()">Move</button>

        <button type="button" (click)="closed.emit()">Close</button>
      </div>
    </div>
  `,
  styles: `
    .modal-backdrop {
      position: fixed;
      inset: 0;
      background: rgb(0 0 0 / 50%);
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .modal-panel {
      background: #fff;
      border-radius: 0.5rem;
      padding: 1.5rem;
      min-width: 20rem;
    }
  `,
})
export class TableDetailModalComponent implements OnInit {
  readonly content = input.required<DetailModalContent>();
  readonly assignedTableIds = input.required<string[]>();
  readonly tables = input.required<TableOption[]>();

  readonly closed = output<void>();
  readonly move = output<string | null>();

  protected readonly moveTarget = signal('');

  protected readonly beerContent = computed(() => {
    const content = this.content();
    return content.kind === 'beer' ? content : null;
  });

  protected readonly judgeContent = computed(() => {
    const content = this.content();
    return content.kind === 'judge' ? content : null;
  });

  protected readonly title = computed(() =>
    this.content().kind === 'beer' ? 'Beer details' : 'Judge details',
  );

  protected readonly abvLabel = computed(() => {
    const beer = this.beerContent();
    if (!beer || (beer.abvLow === null && beer.abvHigh === null)) {
      return null;
    }
    return `${beer.abvLow ?? '?'}–${beer.abvHigh ?? '?'}%`;
  });

  protected readonly assignedTableNames = computed(() => {
    const ids = new Set(this.assignedTableIds());
    if (ids.size === 0) {
      return 'Unassigned';
    }
    const names = this.tables()
      .filter((table) => ids.has(table.id))
      .map((table) => table.name);
    return names.length > 0 ? names.join(', ') : 'Unassigned';
  });

  ngOnInit(): void {
    this.moveTarget.set(this.assignedTableIds()[0] ?? '');
  }

  protected onMove(): void {
    const value = this.moveTarget();
    this.move.emit(value === '' ? null : value);
  }
}
