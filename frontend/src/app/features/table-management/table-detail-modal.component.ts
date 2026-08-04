import { CdkTrapFocus } from '@angular/cdk/a11y';
import type { OnInit } from '@angular/core';
import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';

import { BpButtonComponent } from '../../shared/components/bp-button/bp-button.component';

export interface BeerDetailContent {
  kind: 'beer';
  id: string;
  blindCode: string;
  styleName: string;
  // Real ABV% of this specific beer entry (distinct from abvLow/abvHigh, the BJCP style's
  // declared range — kept alongside it as a secondary/contextual field).
  abvPercent: number;
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
  imports: [CdkTrapFocus, BpButtonComponent],
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

        <label class="move-label">
          Move to
          <select
            class="move-select"
            [value]="moveTarget()"
            (change)="moveTarget.set($any($event.target).value)"
          >
            <option value="">Unassigned</option>
            @for (table of tables(); track table.id) {
              <option [value]="table.id">{{ table.name }}</option>
            }
          </select>
        </label>
        <div class="modal-actions">
          <bp-button
            type="button"
            label="Move"
            variant="secondary"
            (clicked)="onMove()"
          ></bp-button>
          <bp-button
            type="button"
            label="Close"
            variant="ghost"
            (clicked)="closed.emit()"
          ></bp-button>
        </div>
      </div>
    </div>
  `,
  styles: `
    .modal-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(4, 23, 18, 0.45);
      display: flex;
      align-items: center;
      justify-content: center;
      padding: var(--spacing-4);
      z-index: 10;
    }

    .modal-panel {
      background: var(--color-bp-surface);
      border-radius: var(--radius-lg);
      box-shadow: var(--shadow-lg);
      padding: var(--spacing-6);
      min-width: 20rem;
    }

    .modal-panel h2 {
      font-family: 'Fraunces', serif;
      font-size: 1.25rem;
      margin: 0 0 var(--spacing-3);
      color: var(--color-bp-text);
    }

    .modal-panel dl {
      margin: 0 0 var(--spacing-4);
    }

    .modal-panel dt {
      font-size: 0.8125rem;
      font-weight: 600;
      color: var(--color-bp-text-muted);
      margin-top: var(--spacing-2);
    }

    .modal-panel dd {
      margin: 0;
      color: var(--color-bp-text);
    }

    .move-label {
      display: block;
      font-size: 0.875rem;
      font-weight: 600;
      color: var(--color-bp-text);
      margin-bottom: var(--spacing-4);
    }

    .move-select {
      display: block;
      width: 100%;
      min-height: 44px;
      margin-top: var(--spacing-2);
      padding: 0 var(--spacing-3);
      border: 1.5px solid var(--color-bp-border-strong);
      border-radius: var(--radius-md);
      background: var(--color-bp-surface);
      color: var(--color-bp-text);
    }

    .modal-actions {
      display: flex;
      gap: var(--spacing-3);
      margin-top: var(--spacing-4);
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

  // Always shows the beer's real ABV% (abvPercent). When the BJCP style's declared range
  // (abvLow/abvHigh) is also available it's appended as secondary/contextual information — e.g.
  // "5.5% (style range 4.5–7.5%)" — otherwise just the real percent is shown on its own.
  protected readonly abvLabel = computed(() => {
    const beer = this.beerContent();
    if (!beer) {
      return null;
    }
    const real = `${beer.abvPercent}%`;
    if (beer.abvLow === null && beer.abvHigh === null) {
      return real;
    }
    return `${real} (style range ${beer.abvLow ?? '?'}–${beer.abvHigh ?? '?'}%)`;
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
