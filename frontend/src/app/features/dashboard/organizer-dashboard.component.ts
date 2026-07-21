import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ApiError } from '../../core/api/api-error';
import { CompetitionsApiService } from '../competition-wizard/competitions-api.service';
import type { CompetitionSummary } from '../competition-wizard/competitions-api.service';

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

function errorMessage(error: ApiError): string {
  return error.detail ?? error.title;
}

// T100/US13: post-login ORGANIZER landing — every competition the caller has created
// (contracts/rest-api.md GET /competitions), so they can resume or start work without knowing or
// typing an internal address. Selecting a Draft competition reopens the setup wizard; anything
// past Draft goes to the tables screen, the closest existing management view until Phase 11/US9
// ships a unified Active+ dashboard.
@Component({
  selector: 'app-organizer-dashboard',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Competitions</h1>

    @if (loadError(); as message) {
      <p role="alert">{{ message }}</p>
    }

    <a routerLink="/organizer/competitions/new" class="new-competition-action">New competition</a>

    @if (!loadError()) {
      @if (competitions().length === 0) {
        <div class="empty-state">
          <h2>No competitions yet</h2>
          <p>Create your first competition to get started.</p>
        </div>
      } @else {
        <ul class="competition-list">
          @for (competition of competitions(); track competition.id) {
            <li>
              <a [routerLink]="destination(competition)" class="competition-list-item">
                <span class="competition-name">{{ competition.name }}</span>
                <span class="competition-venue">{{ competition.venue }}</span>
                <span class="competition-dates"
                  >{{ competition.startDate }} – {{ competition.endDate }}</span
                >
                <span [class]="badgeClass(competition.state)">{{ competition.state }}</span>
              </a>
            </li>
          }
        </ul>
      }
    }
  `,
  styles: `
    .new-competition-action {
      display: inline-block;
      margin: 1rem 0;
      padding: 0.5rem 1rem;
      border-radius: 0.375rem;
      background: #2563eb;
      color: #fff;
      text-decoration: none;
      font-weight: 600;
    }

    .empty-state {
      padding: 2rem 1rem;
      text-align: center;
      color: #4b5563;
    }

    .competition-list {
      list-style: none;
      margin: 1rem 0 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }

    .competition-list-item {
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

    .competition-name {
      font-weight: 600;
    }

    .competition-venue,
    .competition-dates {
      color: #4b5563;
    }

    .badge {
      margin-left: auto;
      padding: 0.15rem 0.5rem;
      border-radius: 9999px;
      font-size: 0.8rem;
    }

    .badge--draft {
      background: #fef3c7;
      color: #92400e;
    }

    .badge--active {
      background: #dcfce7;
      color: #166534;
    }

    .badge--inevaluation {
      background: #dbeafe;
      color: #1e40af;
    }

    .badge--finalized {
      background: #e5e7eb;
      color: #374151;
    }
  `,
})
export class OrganizerDashboardComponent {
  private readonly api = inject(CompetitionsApiService);

  protected readonly competitions = signal<CompetitionSummary[]>([]);
  protected readonly loadError = signal<string | null>(null);

  constructor() {
    this.loadCompetitions();
  }

  private loadCompetitions(): void {
    this.loadError.set(null);
    this.api.list().subscribe({
      next: (competitions) => this.competitions.set(competitions),
      error: (error: unknown) => this.loadError.set(errorMessage(toGenericApiError(error))),
    });
  }

  protected destination(competition: CompetitionSummary): unknown[] {
    return competition.state === 'Draft'
      ? ['/organizer', 'competitions', competition.id]
      : ['/organizer', 'competitions', competition.id, 'tables'];
  }

  protected badgeClass(state: CompetitionSummary['state']): string {
    return `badge badge--${state.toLowerCase()}`;
  }
}
