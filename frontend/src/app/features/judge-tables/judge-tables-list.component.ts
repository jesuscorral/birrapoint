import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ApiError } from '../../core/api/api-error';
import { TastingOrderApiService } from './tasting-order-api.service';
import type { JudgeTableSummary } from './tasting-order-api.service';

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

function errorMessage(error: ApiError): string {
  return error.detail ?? error.title;
}

// T053/US6: post-login JUDGE landing — every table the caller is actively assigned to, across
// every competition that has left Draft (contracts/rest-api.md GET /me/tables). Selecting a
// table navigates into its blind sample/order view.
@Component({
  selector: 'app-judge-tables-list',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>My tables</h1>

    @if (loadError(); as message) {
      <p role="alert">{{ message }}</p>
    }

    @if (!loadError() && tables().length === 0) {
      <p>No tables assigned yet.</p>
    }

    <ul class="table-list">
      @for (table of tables(); track table.tableId) {
        <li>
          <a [routerLink]="['/judge', 'tables', table.tableId]" class="table-list-item">
            <span class="table-name">{{ table.name }}</span>
            <span class="table-state">{{ table.competitionState }} · {{ table.tableState }}</span>
            @if (table.orderFixed) {
              <span class="badge badge--fixed">
                Order fixed{{ table.orderFixedBy ? ' by ' + table.orderFixedBy : '' }}
              </span>
            } @else {
              <span class="badge badge--pending">Order not fixed</span>
            }
          </a>
        </li>
      }
    </ul>
  `,
  styles: `
    .table-list {
      list-style: none;
      margin: 1rem 0 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }

    .table-list-item {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem;
      padding: 0.75rem 1rem;
      border: 1px solid #d1d5db;
      border-radius: 0.5rem;
      text-decoration: none;
      color: inherit;
    }

    .table-name {
      font-weight: 600;
    }

    .table-state {
      color: #4b5563;
    }

    .badge {
      margin-left: auto;
      padding: 0.15rem 0.5rem;
      border-radius: 9999px;
      font-size: 0.8rem;
    }

    .badge--fixed {
      background: #dcfce7;
      color: #166534;
    }

    .badge--pending {
      background: #fef3c7;
      color: #92400e;
    }
  `,
})
export class JudgeTablesListComponent {
  private readonly api = inject(TastingOrderApiService);

  protected readonly tables = signal<JudgeTableSummary[]>([]);
  protected readonly loadError = signal<string | null>(null);

  constructor() {
    this.loadTables();
  }

  private loadTables(): void {
    this.loadError.set(null);
    this.api.getMyTables().subscribe({
      next: (tables) => this.tables.set(tables),
      error: (error: unknown) => this.loadError.set(errorMessage(toGenericApiError(error))),
    });
  }
}
