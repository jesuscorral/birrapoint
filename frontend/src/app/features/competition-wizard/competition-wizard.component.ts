import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { CompetitionsApiService } from '../../core/api/competitions-api.service';
import type { CompetitionDetail } from '../../core/api/competitions-api.service';
import { BpTopbarComponent } from '../../shared/components/bp-topbar/bp-topbar.component';
import { BasicsStepComponent } from './steps/basics-step.component';
import { CategoriesStepComponent } from './steps/categories-step.component';
import { DetailsStepComponent } from './steps/details-step.component';
import { ImportStepComponent } from './steps/import-step.component';

@Component({
  selector: 'app-competition-wizard',
  imports: [
    BpTopbarComponent,
    BasicsStepComponent,
    DetailsStepComponent,
    CategoriesStepComponent,
    ImportStepComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="wizard-shell">
      <bp-topbar homeLink="/organizer/dashboard"></bp-topbar>

      <main class="wizard-main">
        <div class="wizard-container">
          <span class="eyebrow">{{
            competitionId() ? 'Editar competición' : 'Crear competición'
          }}</span>
          <h1 class="wizard-title">
            {{ competition()?.name || 'Registra tu competición' }}
          </h1>

          <!-- Stepper -->
          <ol class="stepper" aria-label="Progreso del asistente">
            <li
              class="stepper__item"
              [class.is-active]="currentStep() === 1"
              [class.is-done]="currentStep() > 1"
            >
              <button
                type="button"
                class="stepper__step"
                [disabled]="!canJumpTo(1)"
                [attr.aria-current]="currentStep() === 1 ? 'step' : null"
                (click)="goToStep(1)"
              >
                <span class="stepper__marker" aria-hidden="true">
                  @if (currentStep() > 1) {
                    <svg
                      width="14"
                      height="14"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      stroke-width="3"
                      stroke-linecap="round"
                      stroke-linejoin="round"
                    >
                      <path d="M20 6 9 17l-5-5" />
                    </svg>
                  } @else {
                    1
                  }
                </span>
                <span class="stepper__label">Datos básicos</span>
              </button>
            </li>
            <li class="stepper__connector" aria-hidden="true"></li>
            <li
              class="stepper__item"
              [class.is-active]="currentStep() === 2"
              [class.is-done]="currentStep() > 2"
            >
              <button
                type="button"
                class="stepper__step"
                [disabled]="!canJumpTo(2)"
                [attr.aria-current]="currentStep() === 2 ? 'step' : null"
                (click)="goToStep(2)"
              >
                <span class="stepper__marker" aria-hidden="true">
                  @if (currentStep() > 2) {
                    <svg
                      width="14"
                      height="14"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      stroke-width="3"
                      stroke-linecap="round"
                      stroke-linejoin="round"
                    >
                      <path d="M20 6 9 17l-5-5" />
                    </svg>
                  } @else {
                    2
                  }
                </span>
                <span class="stepper__label">Detalles</span>
              </button>
            </li>
            <li class="stepper__connector" aria-hidden="true"></li>
            <li
              class="stepper__item"
              [class.is-active]="currentStep() === 3"
              [class.is-done]="currentStep() > 3"
            >
              <button
                type="button"
                class="stepper__step"
                [disabled]="!canJumpTo(3)"
                [attr.aria-current]="currentStep() === 3 ? 'step' : null"
                (click)="goToStep(3)"
              >
                <span class="stepper__marker" aria-hidden="true">
                  @if (currentStep() > 3) {
                    <svg
                      width="14"
                      height="14"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      stroke-width="3"
                      stroke-linecap="round"
                      stroke-linejoin="round"
                    >
                      <path d="M20 6 9 17l-5-5" />
                    </svg>
                  } @else {
                    3
                  }
                </span>
                <span class="stepper__label">Estilos</span>
              </button>
            </li>
            <li class="stepper__connector" aria-hidden="true"></li>
            <li class="stepper__item" [class.is-active]="currentStep() === 4">
              <button
                type="button"
                class="stepper__step"
                [disabled]="!canJumpTo(4)"
                [attr.aria-current]="currentStep() === 4 ? 'step' : null"
                (click)="goToStep(4)"
              >
                <span class="stepper__marker" aria-hidden="true">4</span>
                <span class="stepper__label">Importar cervezas</span>
              </button>
            </li>
          </ol>

          <div class="wizard-card">
            @if (loading()) {
              <p class="wizard-loading" role="status">Cargando…</p>
            } @else if (loadError()) {
              <p class="wizard-loading" role="alert">No hemos podido cargar esta competición.</p>
            } @else {
              @switch (currentStep()) {
                @case (1) {
                  <app-basics-step
                    [competitionId]="competitionId()"
                    [initialValue]="competition()"
                    (saved)="onBasicsSaved($event)"
                  />
                }
                @case (2) {
                  <app-details-step
                    [competitionId]="competitionId()!"
                    [initialValue]="competition()"
                    (saved)="onDetailsSaved($event)"
                    (back)="onBack()"
                  />
                }
                @case (3) {
                  <app-categories-step
                    [competitionId]="competitionId()!"
                    (saved)="onCategoriesSaved()"
                    (back)="onBack()"
                  />
                }
                @case (4) {
                  <app-import-step
                    [competitionId]="competitionId()!"
                    [importId]="importId()"
                    (importIdChange)="importId.set($event)"
                    (back)="onBack()"
                  />
                }
              }
            }
          </div>
        </div>
      </main>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
        min-height: 100vh;
        background: var(--color-bp-hueso-50);
      }

      .wizard-shell {
        min-height: 100vh;
      }

      .wizard-main {
        display: flex;
        justify-content: center;
        padding: var(--spacing-10) var(--spacing-6) var(--spacing-16);
      }

      .wizard-container {
        width: 100%;
        max-width: 40rem;
      }

      .eyebrow {
        display: block;
        font-size: 0.75rem;
        font-weight: 600;
        letter-spacing: 0.09em;
        text-transform: uppercase;
        color: var(--color-bp-cobre-600);
        margin-bottom: var(--spacing-2);
      }

      .wizard-title {
        font-family: 'Fraunces', serif;
        font-size: 1.75rem;
        line-height: 1.2;
        font-weight: 600;
        letter-spacing: -0.02em;
        color: var(--color-bp-text);
        margin: 0 0 var(--spacing-8);
      }

      /* --- Stepper --- */
      .stepper {
        display: flex;
        align-items: center;
        list-style: none;
        margin: 0 0 var(--spacing-8);
        padding: 0;
      }

      .stepper__item {
        display: flex;
        align-items: center;
        flex: none;
      }

      .stepper__step {
        display: flex;
        align-items: center;
        gap: var(--spacing-3);
        border: none;
        background: none;
        padding: 0;
        font: inherit;
        cursor: pointer;
      }

      .stepper__step:disabled {
        cursor: not-allowed;
        opacity: 0.5;
      }

      .stepper__step:focus-visible {
        outline: 2px solid var(--color-bp-cobre-500);
        outline-offset: 2px;
      }

      .stepper__marker {
        display: grid;
        place-items: center;
        width: 32px;
        height: 32px;
        flex: none;
        border-radius: 50%;
        font-size: 0.875rem;
        font-weight: 700;
        background: var(--color-bp-hueso-200);
        color: var(--color-bp-text-muted);
        border: 1.5px solid var(--color-bp-border-strong);
        transition: all 0.15s ease;
      }

      .stepper__item.is-active .stepper__marker {
        background: var(--color-bp-cobre-500);
        border-color: var(--color-bp-cobre-500);
        color: #fff;
      }

      .stepper__item.is-done .stepper__marker {
        background: var(--color-bp-exito-600);
        border-color: var(--color-bp-exito-600);
        color: #fff;
      }

      .stepper__label {
        font-size: 0.875rem;
        font-weight: 600;
        color: var(--color-bp-text-muted);
      }

      .stepper__item.is-active .stepper__label,
      .stepper__item.is-done .stepper__label {
        color: var(--color-bp-text);
      }

      .stepper__connector {
        flex: 1 1 auto;
        height: 1.5px;
        background: var(--color-bp-border-strong);
        margin: 0 var(--spacing-3);
        min-width: 24px;
      }

      @media (max-width: 480px) {
        .stepper__label {
          display: none;
        }
      }

      /* --- Card --- */
      .wizard-card {
        background: var(--color-bp-surface);
        border: 1px solid var(--color-bp-border);
        border-radius: var(--radius-lg);
        box-shadow: var(--shadow-md);
        padding: var(--spacing-8);
      }

      @media (max-width: 640px) {
        .wizard-card {
          padding: var(--spacing-6);
        }
      }

      .wizard-loading {
        margin: 0;
        color: var(--color-bp-text-muted);
        text-align: center;
        padding: var(--spacing-8) 0;
      }
    `,
  ],
})
export class CompetitionWizardComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly api = inject(CompetitionsApiService);

  protected readonly currentStep = signal<1 | 2 | 3 | 4>(1);
  protected readonly competitionId = signal<string | null>(null);
  protected readonly competition = signal<CompetitionDetail | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal(false);
  // Hoisted here (rather than left as ImportStepComponent local state) because @switch
  // destroys/recreates the non-matching step child on every navigation — this signal is the only
  // thing that survives a step-4 -> step-3 -> step-4 round trip (e.g. to fix a category/style
  // assignment) so the import step can revalidate its pending batch instead of losing it.
  protected readonly importId = signal<string | null>(null);

  constructor() {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.competitionId.set(id);
      this.loading.set(true);
      this.api.getById(id).subscribe({
        next: (detail) => {
          this.competition.set(detail);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.loadError.set(true);
        },
      });
    }
  }

  protected onBasicsSaved(detail: CompetitionDetail): void {
    const isNew = this.competitionId() === null;
    this.competitionId.set(detail.id);
    this.competition.set(detail);
    if (isNew) {
      // Location.replaceState only swaps the address bar/history entry, not the Router's active
      // route — a router.navigate here would recreate this component (different Route config for
      // /new vs /:id) and lose currentStep/competition state. This still satisfies "a reload lands
      // back on the same wizard" since a fresh page load reads the real browser URL.
      this.location.replaceState(`/organizer/competitions/${detail.id}`);
    }
    this.currentStep.set(2);
  }

  protected onDetailsSaved(detail: CompetitionDetail): void {
    this.competition.set(detail);
    this.currentStep.set(3);
  }

  protected onCategoriesSaved(): void {
    this.currentStep.set(4);
  }

  protected onBack(): void {
    this.currentStep.update((step) => (step - 1) as 1 | 2 | 3 | 4);
  }

  protected canJumpTo(step: 1 | 2 | 3 | 4): boolean {
    return step === 1 || this.competitionId() !== null;
  }

  protected goToStep(step: 1 | 2 | 3 | 4): void {
    if (!this.canJumpTo(step)) return;
    this.currentStep.set(step);
  }
}
