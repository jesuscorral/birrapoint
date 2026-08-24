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
  imports: [ReactiveFormsModule, BpButtonComponent, BpInputComponent, BpAlertComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="step-lead">
      Solo lo imprescindible para arrancar: nombre, sede y fechas. Todo lo demás lo completas en el
      siguiente paso, y puedes volver a editarlo cuando quieras mientras la competición esté en
      borrador.
    </p>

    <form [formGroup]="form" (ngSubmit)="onNext()">
      <div class="form-grid">
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

      <!-- Step 1 is the only step with no previous step to go back to (and structurally it can't
           have one — every other step needs the competition id this step creates), so the bottom
           bar here holds only "Siguiente". Every other wizard step follows the same "Atrás" /
           "Siguiente" bottom-bar shape — see details-step.component.ts and onward. -->
      <div class="step-actions">
        <bp-button
          type="submit"
          label="Siguiente"
          variant="primary"
          [loading]="submitting()"
          [disabled]="form.invalid"
        ></bp-button>
      </div>
    </form>
  `,
  styles: [
    `
      .step-lead {
        margin: 0 0 var(--spacing-6);
        color: var(--color-bp-text-muted);
        font-size: 0.9375rem;
      }

      /* Two field columns so the form fills the same card width every other step uses instead of
         stretching single controls across it. Row spacing comes from each field's own
         margin-bottom, so only the column gap is set here. */
      .form-grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        column-gap: var(--spacing-6);
      }

      @media (max-width: 768px) {
        .form-grid {
          grid-template-columns: 1fr;
        }
      }

      .step-actions {
        display: flex;
        justify-content: flex-end;
        align-items: center;
        margin: 0 calc(-1 * var(--spacing-8)) calc(-1 * var(--spacing-8));
        padding: var(--spacing-4) var(--spacing-8) var(--spacing-6);
        border-top: 1px solid var(--color-bp-border);
        position: sticky;
        bottom: 0;
        background: var(--color-bp-surface);
        z-index: 1;
      }

      @media (max-width: 640px) {
        .step-actions {
          margin: 0 calc(-1 * var(--spacing-6)) calc(-1 * var(--spacing-6));
          padding: var(--spacing-4) var(--spacing-6) var(--spacing-6);
        }
      }
    `,
  ],
})
export class BasicsStepComponent {
  private readonly api = inject(CompetitionsApiService);

  readonly competitionId = input<string | null>(null);
  readonly initialValue = input<CompetitionDetail | null>(null);
  readonly saved = output<CompetitionDetail>();
  // Lets the wizard shell prompt before discarding this step's in-progress edits when the
  // organizer jumps to another step via the stepper (FR-007). Driven off FormGroup.dirty, which
  // is a plain getter (not a signal) — only real user input marks a control dirty, so the
  // constructor's own patchValue-from-initialValue effect below never trips a false positive.
  readonly dirtyChange = output<boolean>();

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

    this.form.valueChanges.subscribe(() => {
      this.dirtyChange.emit(this.form.dirty);
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
}
