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
import { BpTextareaComponent } from '../../../shared/components/bp-textarea/bp-textarea.component';
import { BpAlertComponent } from '../../../shared/components/bp-alert/bp-alert.component';

function registrationEndNotBeforeStart(group: AbstractControl): ValidationErrors | null {
  const start = group.get('registrationStart')?.value as string;
  const end = group.get('registrationEnd')?.value as string;
  if (!start || !end) {
    return null;
  }
  return end >= start ? null : { registrationEndBeforeStart: true };
}

type BasicFields = Pick<CompetitionPayload, 'name' | 'venue' | 'startDate' | 'endDate'>;

function extractBasicFields(detail: CompetitionDetail | null): BasicFields {
  return {
    name: detail?.name ?? '',
    venue: detail?.venue ?? '',
    startDate: detail?.startDate ?? '',
    endDate: detail?.endDate ?? '',
  };
}

function toGenericApiError(error: unknown): ApiError {
  return error instanceof ApiError
    ? error
    : new ApiError({ status: 0, title: 'An unexpected error occurred.', urn: null });
}

@Component({
  selector: 'app-details-step',
  imports: [
    ReactiveFormsModule,
    BpButtonComponent,
    BpInputComponent,
    BpTextareaComponent,
    BpAlertComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p class="step-lead">
      Todo esto es opcional y puedes volver a editarlo cuando quieras mientras la competición esté
      en borrador.
    </p>

    <form [formGroup]="form" (ngSubmit)="onSaveDraft()">
      <bp-textarea
        id="details-description"
        label="Descripción"
        formControlName="description"
        placeholder="Cuéntale a jueces y participantes de qué va esta competición…"
        [hasError]="!!fieldError('description')"
        [errorMessage]="fieldError('description') || ''"
      ></bp-textarea>

      <bp-input
        id="details-logo"
        label="URL del logo"
        type="url"
        formControlName="logoUrl"
        placeholder="https://…"
        [hasError]="!!fieldError('logoUrl')"
        [errorMessage]="fieldError('logoUrl') || ''"
      ></bp-input>

      <bp-input
        id="details-entry-limit"
        label="Límite de inscripciones"
        type="number"
        [min]="1"
        formControlName="entryLimit"
        placeholder="Sin límite"
        hint="Deja en blanco si no quieres poner tope."
        [hasError]="!!fieldError('entryLimit') || !!form.controls.entryLimit.errors?.['min']"
        [errorMessage]="
          fieldError('entryLimit') ||
          (form.controls.entryLimit.errors?.['min'] ? 'Debe ser mayor que cero.' : '')
        "
      ></bp-input>

      <div class="field-row">
        <bp-input
          id="details-reg-start"
          label="Inicio de inscripciones"
          type="date"
          formControlName="registrationStart"
          [hasError]="!!fieldError('registrationStart')"
          [errorMessage]="fieldError('registrationStart') || ''"
        ></bp-input>

        <bp-input
          id="details-reg-end"
          label="Fin de inscripciones"
          type="date"
          formControlName="registrationEnd"
          [hasError]="
            !!fieldError('registrationEnd') || form.errors?.['registrationEndBeforeStart']
          "
          [errorMessage]="
            fieldError('registrationEnd') ||
            (form.errors?.['registrationEndBeforeStart']
              ? 'Debe ser igual o posterior al inicio de inscripciones.'
              : '')
          "
        ></bp-input>
      </div>

      @if (bannerError(); as message) {
        <bp-alert type="error" title="No hemos podido guardar">{{ message }}</bp-alert>
      }

      <div class="step-actions">
        <bp-button
          type="button"
          label="← Volver"
          variant="ghost"
          (clicked)="back.emit()"
        ></bp-button>
        <bp-button
          type="submit"
          label="Guardar borrador"
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
export class DetailsStepComponent {
  private readonly api = inject(CompetitionsApiService);

  readonly competitionId = input.required<string>();
  readonly initialValue = input<CompetitionDetail | null>(null);
  readonly saved = output<CompetitionDetail>();
  readonly back = output<void>();
  // See basics-step.component.ts for why this is driven off FormGroup.dirty via valueChanges
  // rather than an effect() over initialValue: patchValue never marks a control dirty.
  readonly dirtyChange = output<boolean>();

  protected readonly form = new FormGroup(
    {
      description: new FormControl(''),
      logoUrl: new FormControl(''),
      entryLimit: new FormControl<number | null>(null, { validators: [Validators.min(1)] }),
      registrationStart: new FormControl(''),
      registrationEnd: new FormControl(''),
    },
    { validators: [registrationEndNotBeforeStart] },
  );

  protected readonly submitting = signal(false);
  protected readonly apiError = signal<ApiError | null>(null);

  constructor() {
    effect(() => {
      const value = this.initialValue();
      if (value) {
        this.form.patchValue({
          description: value.description ?? '',
          logoUrl: value.logoUrl ?? '',
          entryLimit: value.entryLimit,
          registrationStart: value.registrationStart ?? '',
          registrationEnd: value.registrationEnd ?? '',
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

  protected onSaveDraft(): void {
    if (this.form.invalid || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.apiError.set(null);

    const raw = this.form.getRawValue();
    const payload: CompetitionPayload = {
      ...extractBasicFields(this.initialValue()),
      description: raw.description ? raw.description : undefined,
      logoUrl: raw.logoUrl ? raw.logoUrl : undefined,
      entryLimit: raw.entryLimit ?? undefined,
      registrationStart: raw.registrationStart ? raw.registrationStart : undefined,
      registrationEnd: raw.registrationEnd ? raw.registrationEnd : undefined,
    };

    this.api.update(this.competitionId(), payload).subscribe({
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
