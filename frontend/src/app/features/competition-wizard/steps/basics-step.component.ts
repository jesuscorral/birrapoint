import { CdkTrapFocus } from '@angular/cdk/a11y';
import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import type { AbstractControl, ValidationErrors } from '@angular/forms';
import { Router } from '@angular/router';

import { ApiError } from '../../../core/api/api-error';
import { CompetitionsApiService } from '../../../core/api/competitions-api.service';
import type {
  CompetitionDetail,
  CompetitionPayload,
} from '../../../core/api/competitions-api.service';
import { BpButtonComponent } from '../../../shared/components/bp-button/bp-button.component';
import { BpInputComponent } from '../../../shared/components/bp-input/bp-input.component';
import { BpAlertComponent } from '../../../shared/components/bp-alert/bp-alert.component';

function endDateNotBeforeStartDate(group: AbstractControl): ValidationErrors | null {
  const start = group.get('startDate')?.value as string;
  const end = group.get('endDate')?.value as string;
  if (!start || !end) {
    return null;
  }
  return end >= start ? null : { endBeforeStart: true };
}

type DetailFields = Pick<
  CompetitionPayload,
  'description' | 'logoUrl' | 'entryLimit' | 'registrationStart' | 'registrationEnd'
>;

// A PUT is a full replace (contracts/rest-api.md §Competitions) — re-submitting basics for an
// existing competition must carry forward whatever step-2 fields are already saved, or Next
// would silently wipe them.
function extractDetailFields(detail: CompetitionDetail | null): DetailFields {
  if (!detail) {
    return {};
  }
  return {
    description: detail.description ?? undefined,
    logoUrl: detail.logoUrl ?? undefined,
    entryLimit: detail.entryLimit ?? undefined,
    registrationStart: detail.registrationStart ?? undefined,
    registrationEnd: detail.registrationEnd ?? undefined,
  };
}

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

@Component({
  selector: 'app-basics-step',
  imports: [
    ReactiveFormsModule,
    CdkTrapFocus,
    BpButtonComponent,
    BpInputComponent,
    BpAlertComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="step-lead">
      Solo lo imprescindible para arrancar: nombre, sede y fechas. Todo lo demás lo completas en el
      siguiente paso, y puedes volver a editarlo cuando quieras mientras la competición esté en
      borrador.
    </p>

    <form [formGroup]="form" (ngSubmit)="onNext()">
      <bp-input
        id="basics-name"
        label="Nombre de la competición"
        formControlName="name"
        [required]="true"
        placeholder="Copa BirraPoint 2026"
        [hasError]="!!fieldError('name')"
        [errorMessage]="fieldError('name') || ''"
      ></bp-input>

      <bp-input
        id="basics-venue"
        label="Sede / ubicación"
        formControlName="venue"
        [required]="true"
        placeholder="Nave de cata, Madrid"
        [hasError]="!!fieldError('venue')"
        [errorMessage]="fieldError('venue') || ''"
      ></bp-input>

      <div class="field-row">
        <bp-input
          id="basics-start"
          label="Fecha de inicio"
          type="date"
          formControlName="startDate"
          [required]="true"
          [hasError]="!!fieldError('startDate')"
          [errorMessage]="fieldError('startDate') || ''"
        ></bp-input>

        <bp-input
          id="basics-end"
          label="Fecha de fin"
          type="date"
          formControlName="endDate"
          [required]="true"
          [hasError]="!!fieldError('endDate') || form.errors?.['endBeforeStart']"
          [errorMessage]="
            fieldError('endDate') ||
            (form.errors?.['endBeforeStart']
              ? 'Debe ser igual o posterior a la fecha de inicio.'
              : '')
          "
        ></bp-input>
      </div>

      @if (bannerError(); as message) {
        <bp-alert type="error" title="No hemos podido guardar">{{ message }}</bp-alert>
      }

      <div class="step-actions">
        <button type="button" class="back-to-list-link" (click)="onRequestBack()">
          ← Volver al listado
        </button>
        <bp-button
          type="submit"
          label="Continuar"
          variant="primary"
          [loading]="submitting()"
          [disabled]="form.invalid"
        ></bp-button>
      </div>
    </form>

    @if (confirmingBack()) {
      <div class="modal-backdrop" role="presentation" (click)="onCancelBackConfirm()">
        <div
          role="alertdialog"
          aria-modal="true"
          aria-label="Cambios sin guardar"
          class="modal-panel"
          cdkTrapFocus
          cdkTrapFocusAutoCapture
          (click)="$event.stopPropagation()"
          (keydown.escape)="onCancelBackConfirm()"
        >
          <h2>¿Guardar los cambios?</h2>
          <p>
            Puedes guardar esta competición como borrador antes de salir, o descartar los cambios y
            volver al listado de competiciones.
          </p>
          @if (form.invalid) {
            <p class="modal-hint">
              Completa nombre, sede y fechas para poder guardar como borrador.
            </p>
          }
          <div class="modal-actions">
            <bp-button
              type="button"
              label="Guardar borrador"
              variant="primary"
              [loading]="submitting()"
              [disabled]="form.invalid"
              (onClick)="onSaveAndLeave()"
            ></bp-button>
            <bp-button
              type="button"
              label="Descartar cambios"
              variant="secondary"
              [disabled]="submitting()"
              (onClick)="onDiscardAndLeave()"
            ></bp-button>
            <bp-button
              type="button"
              label="Cancelar"
              variant="ghost"
              [disabled]="submitting()"
              (onClick)="onCancelBackConfirm()"
            ></bp-button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [
    `
      .step-lead {
        margin: 0 0 var(--spacing-6);
        color: var(--color-bp-text-muted);
        font-size: 0.9375rem;
      }

      .field-row {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: var(--spacing-4);
      }

      @media (max-width: 480px) {
        .field-row {
          grid-template-columns: 1fr;
        }
      }

      .step-actions {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-top: var(--spacing-6);
        padding-top: var(--spacing-6);
        border-top: 1px solid var(--color-bp-border);
      }

      .back-to-list-link {
        display: inline-flex;
        align-items: center;
        min-height: 44px;
        padding: 0 var(--spacing-3);
        border-radius: var(--radius-md);
        color: var(--color-bp-text-muted);
        font-weight: 600;
        text-decoration: none;
        transition:
          background 0.15s ease,
          color 0.15s ease;
      }

      .back-to-list-link:hover {
        background: var(--color-bp-hueso-100);
        color: var(--color-bp-text);
      }

      .back-to-list-link:focus-visible {
        outline: 2px solid var(--color-bp-cobre-500);
        outline-offset: 2px;
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

      .modal-hint {
        color: var(--color-bp-danger-600) !important;
        font-size: 0.875rem;
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
export class BasicsStepComponent {
  private readonly api = inject(CompetitionsApiService);
  private readonly router = inject(Router);

  readonly competitionId = input<string | null>(null);
  readonly initialValue = input<CompetitionDetail | null>(null);
  readonly saved = output<CompetitionDetail>();

  protected readonly confirmingBack = signal(false);

  protected readonly form = new FormGroup(
    {
      name: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      venue: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      startDate: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      endDate: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    },
    { validators: [endDateNotBeforeStartDate] },
  );

  protected readonly submitting = signal(false);
  protected readonly apiError = signal<ApiError | null>(null);

  constructor() {
    effect(() => {
      const value = this.initialValue();
      if (value) {
        this.form.patchValue({
          name: value.name,
          venue: value.venue,
          startDate: value.startDate,
          endDate: value.endDate,
        });
      }
    });
  }

  protected fieldError(field: string): string | null {
    return this.apiError()?.errors?.[field]?.[0] ?? null;
  }

  protected bannerError(): string | null {
    const error = this.apiError();
    if (!error || error.errors) {
      return null;
    }
    return error.detail ?? error.title;
  }

  protected onNext(): void {
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.apiError.set(null);

    const basics = this.form.getRawValue();
    const payload: CompetitionPayload = { ...basics, ...extractDetailFields(this.initialValue()) };
    const id = this.competitionId();
    const request = id ? this.api.update(id, payload) : this.api.create(payload);

    request.subscribe({
      next: (detail) => {
        this.submitting.set(false);
        this.saved.emit(detail);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.apiError.set(toGenericApiError(error));
      },
    });
  }

  protected onRequestBack(): void {
    this.confirmingBack.set(true);
  }

  protected onCancelBackConfirm(): void {
    this.confirmingBack.set(false);
  }

  protected onDiscardAndLeave(): void {
    this.confirmingBack.set(false);
    this.router.navigateByUrl('/organizer/dashboard');
  }

  protected onSaveAndLeave(): void {
    if (this.form.invalid || this.submitting()) {
      return;
    }

    // Close the dialog before the request resolves rather than after: on success there's nothing
    // left to confirm, and on failure the underlying step is what shows the error (banner or
    // per-field, same as onNext) — a dialog with no error slot of its own would otherwise hide it.
    this.confirmingBack.set(false);
    this.submitting.set(true);
    this.apiError.set(null);

    const basics = this.form.getRawValue();
    const payload: CompetitionPayload = { ...basics, ...extractDetailFields(this.initialValue()) };
    const id = this.competitionId();
    const request = id ? this.api.update(id, payload) : this.api.create(payload);

    request.subscribe({
      next: () => {
        this.submitting.set(false);
        this.router.navigateByUrl('/organizer/dashboard');
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.apiError.set(toGenericApiError(error));
      },
    });
  }
}
