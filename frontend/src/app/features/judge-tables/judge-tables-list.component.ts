import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ApiError } from '../../core/api/api-error';
import { BpPageShellComponent } from '../../core/layout/bp-page-shell/bp-page-shell.component';
import { TastingOrderApiService } from './tasting-order-api.service';
import type { JudgeTableSummary } from './tasting-order-api.service';

interface EjectionNavigationState {
  ejected?: boolean;
  tableName?: string;
}

function readEjectionNotice(): { tableName: string | null } | null {
  const state = history.state as EjectionNavigationState | null;
  if (!state?.ejected) {
    return null;
  }
  return { tableName: state.tableName ?? null };
}

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'Ha ocurrido un error inesperado.', urn: null });
}

function errorMessage(error: ApiError): string {
  return error.detail ?? error.title;
}

// T053/US6: post-login JUDGE landing — every table the caller is actively assigned to, across
// every competition that has left Draft (contracts/rest-api.md GET /me/tables). Selecting a
// table navigates into its blind sample/order view.
//
// Session 2026-09-20: wrapped in bp-page-shell (Ajustes/Cerrar sesión/Cambiar rol now fixed here
// too, not just on the organizer dashboard) — the standalone "Cambiar rol" button this screen
// used to carry its own copy of moved into BpPageShellComponent itself.
@Component({
  selector: 'app-judge-tables-list',
  imports: [RouterLink, BpPageShellComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <bp-page-shell homeLink="/judge/tables">
      <h1>Mis mesas</h1>

      @if (ejectionNotice(); as notice) {
        <p role="status" class="ejection-banner">
          El organizador te ha eliminado de {{ notice.tableName ?? 'una mesa' }}.
          <button type="button" (click)="dismissEjectionNotice()">Descartar</button>
        </p>
      }

      @if (loadError(); as message) {
        <p role="alert">{{ message }}</p>
      }

      @if (loading()) {
        <p role="status">Cargando…</p>
      } @else if (!loadError() && tables().length === 0) {
        <p>Todavía no tienes mesas asignadas.</p>
      }

      <ul class="table-list">
        @for (table of tables(); track table.tableId) {
          <li>
            <a [routerLink]="['/judge', 'tables', table.tableId]" class="table-list-item">
              <span class="table-name">{{ table.name }}</span>
              <span class="table-state">{{ table.competitionState }} · {{ table.tableState }}</span>
              @if (table.orderFixed) {
                <span class="badge badge--fixed">
                  Orden fijado{{ table.orderFixedBy ? ' por ' + table.orderFixedBy : '' }}
                </span>
              } @else {
                <span class="badge badge--pending">Orden sin fijar</span>
              }
            </a>
          </li>
        }
      </ul>
    </bp-page-shell>
  `,
  styles: `
    .ejection-banner {
      background: #fef3c7;
      color: #92400e;
      padding: 0.5rem 0.75rem;
      border-radius: 0.5rem;
      font-weight: 600;
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }

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
  protected readonly loading = signal(true);

  // T087/US12: a one-time notice read off `history.state`, set by a component that redirected
  // here after this judge was live-removed from a table (judge-table-order.component.ts,
  // evaluation-sheet.component.ts). Doesn't need to survive a reload — read once at construction.
  protected readonly ejectionNotice = signal(readEjectionNotice());

  constructor() {
    this.loadTables();
  }

  protected dismissEjectionNotice(): void {
    this.ejectionNotice.set(null);
  }

  private loadTables(): void {
    this.loadError.set(null);
    this.loading.set(true);
    this.api.getMyTables().subscribe({
      next: (tables) => {
        this.tables.set(tables);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loadError.set(errorMessage(toGenericApiError(error)));
        this.loading.set(false);
      },
    });
  }
}
