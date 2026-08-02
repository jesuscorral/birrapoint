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
    :host {
      display: block;
      min-height: 100vh;
      background: var(--color-bp-hueso-50);
      padding: var(--spacing-8) var(--spacing-6);
    }

    h1 {
      font-family: 'Fraunces', serif;
      font-size: 1.75rem;
      font-weight: 600;
      letter-spacing: -0.02em;
      color: var(--color-bp-text);
      margin: 0 0 var(--spacing-4);
    }

    .new-competition-action {
      display: inline-flex;
      align-items: center;
      min-height: 44px;
      margin: var(--spacing-2) 0 var(--spacing-6);
      padding: 0 var(--spacing-5);
      border-radius: var(--radius-md);
      background: var(--color-bp-cobre-500);
      color: #fff;
      text-decoration: none;
      font-weight: 600;
      box-shadow: var(--shadow-sm);
      transition: background 0.15s ease;
    }

    .new-competition-action:hover {
      background: var(--color-bp-cobre-600);
    }

    .new-competition-action:focus-visible {
      outline: none;
      box-shadow:
        0 0 0 3px var(--color-bp-hueso-50),
        0 0 0 5px var(--color-bp-cobre-500);
    }

    .empty-state {
      padding: var(--spacing-12) var(--spacing-4);
      text-align: center;
      color: var(--color-bp-text-muted);
      background: var(--color-bp-surface);
      border: 1px solid var(--color-bp-border);
      border-radius: var(--radius-lg);
    }

    .empty-state h2 {
      font-family: 'Fraunces', serif;
      color: var(--color-bp-text);
      margin: 0 0 var(--spacing-2);
    }

    .competition-list {
      list-style: none;
      margin: var(--spacing-4) 0 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: var(--spacing-3);
    }

    .competition-list-row {
      display: flex;
      align-items: center;
      gap: var(--spacing-3);
    }

    .competition-list-item {
      flex: 1 1 auto;
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--spacing-3);
      padding: var(--spacing-4);
      background: var(--color-bp-surface);
      border: 1px solid var(--color-bp-border);
      border-radius: var(--radius-lg);
      text-decoration: none;
      color: inherit;
      transition:
        border-color 0.15s ease,
        box-shadow 0.15s ease;
    }

    .competition-list-item:hover {
      border-color: var(--color-bp-cobre-400);
      box-shadow: var(--shadow-sm);
    }

    .competition-list-item:focus-visible {
      outline: none;
      box-shadow:
        0 0 0 3px var(--color-bp-hueso-50),
        0 0 0 5px var(--color-bp-cobre-500);
    }

    .competition-name {
      font-weight: 700;
      color: var(--color-bp-text);
    }

    .competition-venue,
    .competition-dates {
      color: var(--color-bp-text-muted);
      font-size: 0.9375rem;
    }

    .badge {
      margin-left: auto;
      padding: 3px 10px;
      border-radius: var(--radius-full);
      font-size: 0.75rem;
      font-weight: 700;
      letter-spacing: 0.02em;
    }

    .badge--draft {
      background: var(--color-bp-cobre-100);
      color: var(--color-bp-cobre-700);
    }

    .badge--active {
      background: var(--color-bp-exito-50);
      color: var(--color-bp-exito-600);
    }

    .badge--inevaluation {
      background: var(--color-bp-info-50);
      color: var(--color-bp-info-600);
    }

    .badge--finalized {
      background: var(--color-bp-hueso-200);
      color: var(--color-bp-text-muted);
    }

    .advance-state-action {
      flex: 0 0 auto;
      min-height: 40px;
      padding: 0 var(--spacing-4);
      border-radius: var(--radius-md);
      border: 1.5px solid var(--color-bp-cobre-500);
      background: var(--color-bp-surface);
      color: var(--color-bp-cobre-600);
      font-weight: 600;
      cursor: pointer;
      transition: background 0.15s ease;
    }

    .advance-state-action:hover {
      background: var(--color-bp-cobre-50);
    }

    .advance-state-action:focus-visible {
      outline: none;
      box-shadow:
        0 0 0 3px var(--color-bp-hueso-50),
        0 0 0 5px var(--color-bp-cobre-500);
    }

    .modal-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(4, 23, 18, 0.45);
      display: flex;
      align-items: center;
      justify-content: center;
      padding: var(--spacing-4);
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

    .modal-panel p {
      color: var(--color-bp-text-muted);
      margin: 0 0 var(--spacing-6);
    }

    .modal-panel button {
      min-height: 40px;
      padding: 0 var(--spacing-4);
      border-radius: var(--radius-md);
      font-weight: 600;
      cursor: pointer;
      margin-right: var(--spacing-3);
    }

    .modal-panel button[type='button']:first-of-type {
      background: var(--color-bp-cobre-500);
      color: #fff;
      border: 1.5px solid var(--color-bp-cobre-500);
    }

    .modal-panel button:last-of-type {
      background: var(--color-bp-surface);
      color: var(--color-bp-text-muted);
      border: 1.5px solid var(--color-bp-border-strong);
    }

    [role='alert'] {
      color: var(--color-bp-danger-600);
      background: var(--color-bp-danger-50);
      border: 1px solid #efc9c5;
      border-radius: var(--radius-md);
      padding: var(--spacing-3) var(--spacing-4);
      margin-bottom: var(--spacing-4);
    }

    [role='status'] {
      color: var(--color-bp-exito-600);
      background: var(--color-bp-exito-50);
      border: 1px solid #c6ddd0;
      border-radius: var(--radius-md);
      padding: var(--spacing-3) var(--spacing-4);
      margin-bottom: var(--spacing-4);
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
