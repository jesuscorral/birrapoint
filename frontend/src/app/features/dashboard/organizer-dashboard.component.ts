import { CdkTrapFocus } from '@angular/cdk/a11y';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ApiError } from '../../core/api/api-error';
import { CompetitionsApiService } from '../../core/api/competitions-api.service';
import type { CompetitionState, CompetitionSummary } from '../../core/api/competitions-api.service';

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

function errorMessage(error: ApiError): string {
  return error.detail ?? error.title;
}

// FR-006 forward-only lifecycle: Draft -> Active -> InEvaluation -> Finalized. Finalized has no
// next state, so no advance control is rendered for it (FR-051).
const NEXT_STATE: Record<CompetitionState, CompetitionState | null> = {
  Draft: 'Active',
  Active: 'InEvaluation',
  InEvaluation: 'Finalized',
  Finalized: null,
};

// Describes what the transition does, not just "Advance" (FR-051).
const ADVANCE_LABEL: Record<CompetitionState, string | null> = {
  Draft: 'Activate',
  Active: 'Start evaluation',
  InEvaluation: 'Finalize',
  Finalized: null,
};

// T100/US13: post-login ORGANIZER landing — every competition the caller has created
// (contracts/rest-api.md GET /competitions), so they can resume or start work without knowing or
// typing an internal address. Selecting a Draft competition reopens the setup wizard; Active goes
// to the tables screen (still the setup/assignment view for that state); InEvaluation and
// Finalized go to the live monitoring dashboard (T070/US9) — there's nothing left to set up once
// evaluation has started.
//
// T102/FR-051: the advance-state action lives as a sibling of the navigation `<a>`, never nested
// inside it — a `<button>` inside an `<a>` is invalid HTML and an accessibility hazard (nested
// interactive controls). The transition is irreversible and forward-only, so it goes through the
// same explicit-confirm pattern as judge-table-order.component.ts's "Fix order": `alertdialog` +
// `cdkTrapFocus`. On success the list is refetched (not locally mutated) so the badge and
// available actions reconcile against the server, same convention as the rest of the codebase.
@Component({
  selector: 'app-organizer-dashboard',
  imports: [RouterLink, CdkTrapFocus],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Competitions</h1>

    @if (loadError(); as message) {
      <p role="alert">{{ message }}</p>
    }
    @if (advanceError(); as message) {
      <p role="alert">{{ message }}</p>
    }
    @if (advanceSuccessMessage(); as message) {
      <p role="status">{{ message }}</p>
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
            <li class="competition-list-row">
              <a [routerLink]="destination(competition)" class="competition-list-item">
                <span class="competition-name">{{ competition.name }}</span>
                <span class="competition-venue">{{ competition.venue }}</span>
                <span class="competition-dates"
                  >{{ competition.startDate }} – {{ competition.endDate }}</span
                >
                <span [class]="badgeClass(competition.state)">{{ competition.state }}</span>
              </a>
              @if (advanceLabel(competition.state); as label) {
                <button
                  type="button"
                  class="advance-state-action"
                  (click)="onRequestAdvance(competition)"
                >
                  {{ label }}
                </button>
              }
            </li>
          }
        </ul>
      }
    }

    @if (confirmingAdvance(); as target) {
      <div class="modal-backdrop" role="presentation" (click)="onCancelAdvanceConfirm()">
        <div
          role="alertdialog"
          aria-modal="true"
          aria-label="Confirm advance competition state"
          class="modal-panel"
          cdkTrapFocus
          cdkTrapFocusAutoCapture
          (click)="$event.stopPropagation()"
          (keydown.escape)="onCancelAdvanceConfirm()"
        >
          <h2>{{ advanceLabel(target.state) }}</h2>
          <p>
            This moves "{{ target.name }}" to {{ nextState(target.state) }} and cannot be undone.
            Continue?
          </p>
          <button type="button" [disabled]="advancing()" (click)="onConfirmAdvance()">
            Confirm
          </button>
          <button type="button" (click)="onCancelAdvanceConfirm()">Cancel</button>
        </div>
      </div>
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

    .competition-list-row {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }

    .competition-list-item {
      flex: 1 1 auto;
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

    .advance-state-action {
      flex: 0 0 auto;
      padding: 0.5rem 1rem;
      border-radius: 0.375rem;
      border: 1px solid #2563eb;
      background: #fff;
      color: #2563eb;
      font-weight: 600;
      cursor: pointer;
    }

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
export class OrganizerDashboardComponent {
  private readonly api = inject(CompetitionsApiService);

  protected readonly competitions = signal<CompetitionSummary[]>([]);
  protected readonly loadError = signal<string | null>(null);

  protected readonly confirmingAdvance = signal<CompetitionSummary | null>(null);
  protected readonly advancing = signal(false);
  protected readonly advanceError = signal<string | null>(null);
  protected readonly advanceSuccessMessage = signal<string | null>(null);

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
    if (competition.state === 'Draft') {
      return ['/organizer', 'competitions', competition.id];
    }
    if (competition.state === 'Active') {
      return ['/organizer', 'competitions', competition.id, 'tables'];
    }
    return ['/organizer', 'competitions', competition.id, 'monitor'];
  }

  protected badgeClass(state: CompetitionSummary['state']): string {
    return `badge badge--${state.toLowerCase()}`;
  }

  protected nextState(state: CompetitionState): CompetitionState | null {
    return NEXT_STATE[state];
  }

  protected advanceLabel(state: CompetitionState): string | null {
    return ADVANCE_LABEL[state];
  }

  protected onRequestAdvance(competition: CompetitionSummary): void {
    this.advanceError.set(null);
    this.advanceSuccessMessage.set(null);
    this.confirmingAdvance.set(competition);
  }

  protected onCancelAdvanceConfirm(): void {
    this.confirmingAdvance.set(null);
  }

  protected onConfirmAdvance(): void {
    const target = this.confirmingAdvance();
    const targetState = target ? this.nextState(target.state) : null;
    if (!target || !targetState || this.advancing()) {
      return;
    }

    this.advancing.set(true);
    this.advanceError.set(null);
    this.advanceSuccessMessage.set(null);

    this.api.changeState(target.id, targetState).subscribe({
      next: () => {
        this.advancing.set(false);
        this.confirmingAdvance.set(null);
        this.advanceSuccessMessage.set(`"${target.name}" advanced to ${targetState}.`);
        this.loadCompetitions();
      },
      error: (error: unknown) => {
        this.advancing.set(false);
        this.confirmingAdvance.set(null);
        this.handleAdvanceError(error);
      },
    });
  }

  private handleAdvanceError(error: unknown): void {
    const apiError = toGenericApiError(error);

    if (apiError.urn === 'urn:birrapoint:tables-still-open') {
      const openTableIds = apiError.extensions['openTableIds'];
      const count = Array.isArray(openTableIds) ? openTableIds.length : null;
      this.advanceError.set(
        count !== null
          ? `${count} table(s) still open — close them before finalizing.`
          : 'Some tables are still open — close them before finalizing.',
      );
      return;
    }

    if (apiError.urn === 'urn:birrapoint:invalid-state-transition') {
      this.advanceError.set(
        "This competition's state was already changed elsewhere. The list has been refreshed.",
      );
      this.loadCompetitions();
      return;
    }

    this.advanceError.set(errorMessage(apiError));
  }
}
