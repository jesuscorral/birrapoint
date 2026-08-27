import { CdkTrapFocus } from '@angular/cdk/a11y';
import { Location } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

import { CompetitionsApiService } from '../../core/api/competitions-api.service';
import type { CompetitionDetail } from '../../core/api/competitions-api.service';
import { BpButtonComponent } from '../../shared/components/bp-button/bp-button.component';
import { BpTopbarComponent } from '../../shared/components/bp-topbar/bp-topbar.component';
import { BasicsStepComponent } from './steps/basics-step.component';
import { CategoriesStepComponent } from './steps/categories-step.component';
import { DetailsStepComponent } from './steps/details-step.component';
import { ImportStepComponent } from './steps/import-step.component';
import { JudgeImportStepComponent } from './steps/judge-import-step.component';
import { TablesStepComponent } from './steps/tables-step.component';

@Component({
  selector: 'app-competition-wizard',
  imports: [
    BpTopbarComponent,
    BpButtonComponent,
    CdkTrapFocus,
    BasicsStepComponent,
    DetailsStepComponent,
    CategoriesStepComponent,
    ImportStepComponent,
    JudgeImportStepComponent,
    TablesStepComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="wizard-shell">
      <bp-topbar homeLink="/organizer/dashboard"></bp-topbar>

      <main class="wizard-main">
        <div class="wizard-container">
          <div class="wizard-header">
            <div>
              <span class="eyebrow">{{
                competitionId() ? 'Editar competición' : 'Crear competición'
              }}</span>
              <h1 class="wizard-title">
                {{ competition()?.name || 'Registra tu competición' }}
              </h1>
            </div>
            <!-- T125: hoisted out of the step action bars, where it existed on steps 1 and 3 only
                 and competed with "Atrás" for the same corner. One exit affordance, same place on
                 all six steps, guarded by the wizard's own unsaved-changes dialog. -->
            <button type="button" class="back-to-list-link" (click)="onRequestExit()">
              ← Volver al listado
            </button>
          </div>

          <!-- Stepper -->
          <ol class="stepper" aria-label="Progreso del asistente">
            @for (step of steps; track step.number) {
              <li
                class="stepper__item"
                [class.is-active]="currentStep() === step.number"
                [class.is-reached]="currentStep() >= step.number"
                [class.is-complete]="stepStatus(step.number) === 'complete'"
                [class.is-partial]="stepStatus(step.number) === 'partial'"
              >
                <button
                  type="button"
                  class="stepper__step"
                  [disabled]="!canJumpTo(step.number)"
                  [attr.aria-current]="currentStep() === step.number ? 'step' : null"
                  (click)="goToStep(step.number)"
                >
                  <span class="stepper__marker" aria-hidden="true">
                    @if (stepStatus(step.number) === 'complete') {
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
                      {{ step.number }}
                    }
                  </span>
                  <span class="stepper__label">{{ step.label }}</span>
                </button>
              </li>
            }
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
                    (dirtyChange)="stepDirty.set($event)"
                  />
                }
                @case (2) {
                  <app-details-step
                    [competitionId]="competitionId()!"
                    [initialValue]="competition()"
                    (saved)="onDetailsSaved($event)"
                    (back)="onBack()"
                    (dirtyChange)="stepDirty.set($event)"
                  />
                }
                @case (3) {
                  <app-categories-step
                    [competitionId]="competitionId()!"
                    (saved)="onCategoriesSaved()"
                    (back)="onBack()"
                    (dirtyChange)="stepDirty.set($event)"
                    (statusChange)="categoriesStatus.set($event)"
                  />
                }
                @case (4) {
                  <app-import-step
                    [competitionId]="competitionId()!"
                    [importId]="importId()"
                    (importIdChange)="importId.set($event)"
                    (saved)="onImportSaved()"
                    (back)="onBack()"
                    (dirtyChange)="stepDirty.set($event)"
                    (statusChange)="importStatus.set($event)"
                  />
                }
                @case (5) {
                  <app-judge-import-step
                    [competitionId]="competitionId()!"
                    [judgeImportId]="judgeImportId()"
                    (judgeImportIdChange)="judgeImportId.set($event)"
                    (saved)="onJudgeImportSaved()"
                    (back)="onBack()"
                    (dirtyChange)="stepDirty.set($event)"
                    (statusChange)="judgeImportStatus.set($event)"
                  />
                }
                @case (6) {
                  <app-tables-step
                    [competitionId]="competitionId()!"
                    (back)="onBack()"
                    (dirtyChange)="stepDirty.set($event)"
                    (statusChange)="tablesStatus.set($event)"
                    (finished)="onRequestExit()"
                  />
                }
              }
            }
          </div>
        </div>
      </main>
    </div>

    @if (pendingStep() !== null || pendingExit()) {
      <div class="modal-backdrop" role="presentation" (click)="onKeepEditing()">
        <div
          role="alertdialog"
          aria-modal="true"
          aria-label="Cambios sin guardar"
          class="modal-panel"
          cdkTrapFocus
          cdkTrapFocusAutoCapture
          (click)="$event.stopPropagation()"
          (keydown.escape)="onKeepEditing()"
        >
          <h2>Cambios sin guardar</h2>
          <p>Este paso tiene cambios que no se han guardado. Si continúas, se perderán.</p>
          <div class="modal-actions">
            <bp-button
              type="button"
              label="Seguir editando"
              variant="primary"
              (clicked)="onKeepEditing()"
            ></bp-button>
            <bp-button
              type="button"
              label="Descartar y continuar"
              variant="secondary"
              (clicked)="onDiscardAndNavigate()"
            ></bp-button>
          </div>
          <p class="modal-hint">Para conservarlos, usa «Guardar borrador» antes de salir.</p>
        </div>
      </div>
    }
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

      /* T125b: real breathing room at the sides. The shell spans the viewport but the content
         never runs up against it — the gutter widens with the screen instead of the card growing
         to fill every last pixel. */
      .wizard-main {
        display: flex;
        justify-content: center;
        padding: var(--spacing-10) var(--spacing-8) var(--spacing-16);
      }

      @media (min-width: 1280px) {
        .wizard-main {
          padding-inline: var(--spacing-12);
        }
      }

      @media (min-width: 1800px) {
        .wizard-main {
          padding-inline: var(--spacing-16);
        }
      }

      @media (max-width: 640px) {
        .wizard-main {
          padding: var(--spacing-8) var(--spacing-4) var(--spacing-12);
        }
      }

      /* T125: the organizer console is desktop-first. The shell now spans the viewport (capped so
         it does not sprawl on ultrawide displays) and each step decides its own inner measure —
         the form steps wrap their fields in .step-form to keep a readable column, while the
         import and table-assignment steps use the full width they actually need. */
      .wizard-container {
        width: 100%;
        max-width: 88rem;
      }

      .wizard-header {
        display: flex;
        align-items: flex-start;
        justify-content: space-between;
        gap: var(--spacing-4);
        flex-wrap: wrap;
      }

      .back-to-list-link {
        border: none;
        background: none;
        padding: var(--spacing-2) 0;
        font: inherit;
        font-size: 0.875rem;
        font-weight: 600;
        color: var(--color-bp-text-muted);
        cursor: pointer;
        text-decoration: underline;
        text-underline-offset: 3px;
      }

      .back-to-list-link:hover {
        color: var(--color-bp-text);
      }

      .back-to-list-link:focus-visible {
        outline: 2px solid var(--color-bp-cobre-500);
        outline-offset: 2px;
        border-radius: var(--radius-sm);
      }

      .eyebrow {
        display: block;
        font-size: 0.75rem;
        font-weight: 600;
        letter-spacing: 0.09em;
        text-transform: uppercase;
        color: var(--color-bp-cobre-700);
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

      .modal-hint {
        margin: var(--spacing-4) 0 0;
        font-size: 0.8125rem;
        color: var(--color-bp-text-muted);
      }

      /* --- Stepper --- */
      .stepper {
        display: grid;
        grid-template-columns: repeat(6, 1fr);
        list-style: none;
        margin: 0 0 var(--spacing-8);
        padding: 0;
      }

      .stepper__item {
        position: relative;
        display: flex;
        justify-content: center;
      }

      /* The connector spans marker-centre to marker-centre minus the marker's outer radius (17px
         with its border) plus a 6px breathing gap, so it stops short of both circles instead of
         running underneath the step numbers. */
      .stepper__item + .stepper__item::before {
        content: '';
        position: absolute;
        top: 15px;
        right: calc(50% + 23px);
        left: calc(-50% + 23px);
        height: 1.5px;
        background: var(--color-bp-border-strong);
      }

      .stepper__item.is-active::before,
      .stepper__item.is-reached::before {
        background: var(--color-bp-exito-600);
      }

      .stepper__step {
        position: relative;
        /* Above the connectors regardless of sibling paint order. */
        z-index: 1;
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--spacing-2);
        width: 100%;
        border: none;
        background: none;
        padding: 0 0.25rem;
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

      /* Never-visited default: no fill, just the outline and the number. */
      .stepper__marker {
        display: grid;
        place-items: center;
        width: 32px;
        height: 32px;
        flex: none;
        border-radius: 50%;
        font-size: 0.875rem;
        font-weight: 700;
        background: transparent;
        color: var(--color-bp-text-muted);
        border: 1.5px solid var(--color-bp-border-strong);
        transition: all 0.15s ease;
      }

      .stepper__item.is-active .stepper__marker {
        background: var(--color-bp-cobre-500);
        border-color: var(--color-bp-cobre-500);
        color: #fff;
      }

      /* Passed and fully filled in. */
      .stepper__item.is-complete .stepper__marker {
        background: var(--color-bp-exito-600);
        border-color: var(--color-bp-exito-600);
        color: #fff;
      }

      /* Passed but still missing data. */
      .stepper__item.is-partial .stepper__marker {
        background: var(--color-bp-aviso-600);
        border-color: var(--color-bp-aviso-600);
        color: #fff;
      }

      .stepper__label {
        font-size: 0.8125rem;
        font-weight: 600;
        line-height: 1.25;
        text-align: center;
        text-wrap: balance;
        color: var(--color-bp-text-muted);
      }

      .stepper__item.is-active .stepper__label,
      .stepper__item.is-complete .stepper__label,
      .stepper__item.is-partial .stepper__label {
        color: var(--color-bp-text);
      }

      @media (max-width: 640px) {
        .stepper__label {
          font-size: 0.6875rem;
        }
      }

      /* --- Card --- */
      .wizard-card {
        background: var(--color-bp-surface);
        border: 1px solid var(--color-bp-border);
        border-radius: var(--radius-lg);
        box-shadow: var(--shadow-md);
        padding: var(--spacing-8);
        /* Read by bp-step-actions so its sticky footer bleeds to the card's own edges. */
        --bp-step-actions-inset: var(--spacing-8);
      }

      @media (max-width: 640px) {
        .wizard-card {
          padding: var(--spacing-6);
          --bp-step-actions-inset: var(--spacing-6);
        }
      }

      .wizard-loading {
        margin: 0;
        color: var(--color-bp-text-muted);
        text-align: center;
        padding: var(--spacing-8) 0;
      }

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
        max-width: 26rem;
      }

      .modal-panel h2 {
        font-family: 'Fraunces', serif;
        font-size: 1.25rem;
        margin: 0 0 var(--spacing-3);
        color: var(--color-bp-text);
      }

      .modal-panel p {
        color: var(--color-bp-text-muted);
        margin: 0 0 var(--spacing-4);
      }

      .modal-actions {
        display: flex;
        flex-wrap: wrap;
        gap: var(--spacing-3);
        margin-top: var(--spacing-6);
      }
    `,
  ],
})
export class CompetitionWizardComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly api = inject(CompetitionsApiService);
  private readonly router = inject(Router);

  protected readonly steps: { number: 1 | 2 | 3 | 4 | 5 | 6; label: string }[] = [
    { number: 1, label: 'Datos básicos' },
    { number: 2, label: 'Detalles' },
    { number: 3, label: 'Estilos' },
    { number: 4, label: 'Importar cervezas' },
    { number: 5, label: 'Importar jueces' },
    { number: 6, label: 'Mesas' },
  ];

  protected readonly currentStep = signal<1 | 2 | 3 | 4 | 5 | 6>(1);
  protected readonly competitionId = signal<string | null>(null);
  protected readonly competition = signal<CompetitionDetail | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal(false);
  // Hoisted here (rather than left as ImportStepComponent local state) because @switch
  // destroys/recreates the non-matching step child on every navigation — this signal is the only
  // thing that survives a step-4 -> step-3 -> step-4 round trip (e.g. to fix a category/style
  // assignment) so the import step can revalidate its pending batch instead of losing it.
  protected readonly importId = signal<string | null>(null);
  // Same reasoning as importId above, for the judge-roster import batch (step 5). There is no
  // revalidate endpoint for judge imports (nothing here resolves against data that can change out
  // from under the batch), but the pending batch id must still survive a step navigation round
  // trip so JudgeImportStepComponent can re-fetch its current state instead of showing an empty
  // upload form again.
  protected readonly judgeImportId = signal<string | null>(null);
  // FR-007: the currently-mounted step's own notion of "has unsaved edits", reported via its
  // dirtyChange output. Read by attemptNavigate() before a Back/stepper jump actually switches
  // currentStep — @switch destroys the leaving step's instance immediately, so this is the only
  // point where in-progress edits can still be caught and confirmed instead of silently lost.
  protected readonly stepDirty = signal(false);
  // Non-null while the "discard unsaved edits?" dialog is open; holds the step we'd move to if
  // the organizer confirms.
  protected readonly pendingStep = signal<1 | 2 | 3 | 4 | 5 | 6 | null>(null);
  // T125's header "Volver al listado" shares pendingStep's dialog: same FR-007 question, different
  // destination, so the two intents are tracked separately but rendered by one alertdialog.
  protected readonly pendingExit = signal(false);

  // Every step number that currentStep has ever landed on this session — the stepper marker for a
  // step colours in (green/orange) only once it's been passed; a step never reached yet stays
  // empty even if it's reachable via the stepper (canJumpTo lets you jump ahead once
  // competitionId is set).
  protected readonly visitedSteps = signal<ReadonlySet<number>>(new Set([1]));

  // Steps 3-6 report their own completeness (each depends on data this shell doesn't otherwise
  // load — categories/styles, import rows, judge rows, table assignments) via a statusChange
  // output, same pattern as the existing dirtyChange wiring. Defaults to 'partial' until the step
  // has actually mounted and reported in, and — because @switch destroys/recreates the
  // non-matching step on every navigation — keeps whatever it last reported after the organizer
  // navigates away.
  protected readonly categoriesStatus = signal<'complete' | 'partial'>('partial');
  protected readonly importStatus = signal<'complete' | 'partial'>('partial');
  protected readonly judgeImportStatus = signal<'complete' | 'partial'>('partial');
  protected readonly tablesStatus = signal<'complete' | 'partial'>('partial');

  // Steps 1-2 write straight to the `competition` record this shell already holds, so their
  // completeness is derived from it directly rather than needing their own statusChange output.
  // Step 1's required fields (name, venue, startDate, endDate — see basics-step.component.ts) are
  // already enforced before the record can be saved, so this is 'complete' the moment the record
  // exists.
  protected readonly basicsStatus = computed<'complete' | 'partial'>(() => {
    const c = this.competition();
    return c && c.name && c.venue && c.startDate && c.endDate ? 'complete' : 'partial';
  });

  // Every field on step 2 is optional (see details-step.component.ts) — there is no required
  // field that can be "missing", so per the completion rule (complete = every required field
  // filled) this step is complete as soon as it's been visited and the competition exists.
  protected readonly detailsStatus = computed<'complete' | 'partial'>(() =>
    this.competition() ? 'complete' : 'partial',
  );

  constructor() {
    // currentStep starts at 1 and visitedSteps is seeded with 1, so this only ever adds steps 2-6
    // as the organizer actually reaches them.
    effect(() => {
      const step = this.currentStep();
      if (!this.visitedSteps().has(step)) {
        this.visitedSteps.update((set) => new Set(set).add(step));
      }
    });

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

  protected onImportSaved(): void {
    this.currentStep.set(5);
  }

  protected onJudgeImportSaved(): void {
    this.currentStep.set(6);
  }

  protected onBack(): void {
    this.attemptNavigate((this.currentStep() - 1) as 1 | 2 | 3 | 4 | 5 | 6);
  }

  protected canJumpTo(step: 1 | 2 | 3 | 4 | 5 | 6): boolean {
    return step === 1 || this.competitionId() !== null;
  }

  // Stepper marker colour for a given step: null keeps the empty, uncoloured look (either it's
  // the active step, still shown with its own copper highlight, or it has never been visited);
  // otherwise 'complete' (green check, every required field on that step is filled) or 'partial'
  // (amber circle with the step number, something required is still missing).
  protected stepStatus(step: 1 | 2 | 3 | 4 | 5 | 6): 'complete' | 'partial' | null {
    if (this.currentStep() === step || !this.visitedSteps().has(step)) {
      return null;
    }
    switch (step) {
      case 1:
        return this.basicsStatus();
      case 2:
        return this.detailsStatus();
      case 3:
        return this.categoriesStatus();
      case 4:
        return this.importStatus();
      case 5:
        return this.judgeImportStatus();
      case 6:
        return this.tablesStatus();
    }
  }

  protected goToStep(step: 1 | 2 | 3 | 4 | 5 | 6): void {
    if (!this.canJumpTo(step)) return;
    this.attemptNavigate(step);
  }

  // FR-007: navigation away from a step with unsaved edits (Back or a stepper jump) prompts the
  // organizer to keep editing or discard and continue, rather than @switch silently destroying
  // the leaving step's in-progress state. A step with no unsaved edits navigates immediately.
  private attemptNavigate(step: 1 | 2 | 3 | 4 | 5 | 6): void {
    if (step === this.currentStep()) return;
    if (this.stepDirty()) {
      this.pendingStep.set(step);
      return;
    }
    this.currentStep.set(step);
  }

  // FR-007 again, for leaving the wizard entirely rather than moving between its steps.
  protected onRequestExit(): void {
    if (this.stepDirty()) {
      this.pendingExit.set(true);
      return;
    }
    this.router.navigateByUrl('/organizer/dashboard');
  }

  protected onKeepEditing(): void {
    this.pendingStep.set(null);
    this.pendingExit.set(false);
  }

  protected onDiscardAndNavigate(): void {
    const step = this.pendingStep();
    const leaving = this.pendingExit();
    this.pendingStep.set(null);
    this.pendingExit.set(false);
    this.stepDirty.set(false);
    if (leaving) {
      this.router.navigateByUrl('/organizer/dashboard');
      return;
    }
    if (step !== null) {
      this.currentStep.set(step);
    }
  }
}
