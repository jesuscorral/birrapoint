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
import { BpStepActionsComponent } from '../../../shared/components/bp-step-actions/bp-step-actions.component';
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
    BpButtonComponent,
    BpStepActionsComponent,
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
      <div class="step-form">
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
      </div>

      <bp-step-actions
        [showBack]="false"
        nextType="submit"
        [nextLoading]="submitting()"
        [nextDisabled]="form.invalid"
      >
        <bp-button
          type="button"
          label="Guardar borrador"
          variant="secondary"
          [loading]="submitting()"
          [disabled]="form.invalid"
          (clicked)="onSaveAndLeave()"
        ></bp-button>
      </bp-step-actions>
    </form>
  `,
  styles: [
    `
      .step-lead {
        margin: 0 0 var(--spacing-6);
        color: var(--color-bp-text-muted);
        font-size: 0.9375rem;
        max-width: 40rem;
        margin-inline: auto;
      }

      /* T125: the wizard shell is full width now, so form steps keep their own readable measure
         here instead of relying on a narrow shell. The action bar stays outside this wrapper: it
         is the card's footer and spans the card's full width on every step. */
      .step-form {
        display: flex;
        flex-direction: column;
        gap: var(--spacing-4);
        max-width: 40rem;
        margin-inline: auto;
      }

      /* The paired date fields sit side by side. This replaces the earlier .form-grid, which the
         templates stopped using — leaving .field-row with no rule at all, so the dates stacked. */
      .field-row {
        display: grid;
        grid-template-columns: 1fr 1fr;
        column-gap: var(--spacing-4);
      }

      @media (max-width: 768px) {
        .field-row {
          grid-template-columns: 1fr;
        }
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

  // T125: "Guardar borrador" now lives in the shared action bar's centre zone instead of inside a
  // leave-confirmation dialog — the organizer can save and step away at any point, and the wizard
  // header's own "Volver al listado" handles the unsaved-changes prompt.
  protected onSaveAndLeave(): void {
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
