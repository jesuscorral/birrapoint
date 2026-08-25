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
    BpStepActionsComponent,
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
      <div class="step-form">
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
      </div>

      <bp-step-actions
        nextType="submit"
        [nextLoading]="submitting()"
        [nextDisabled]="form.invalid"
        (back)="back.emit()"
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
    `,
  ],
})
export class DetailsStepComponent {
  private readonly api = inject(CompetitionsApiService);
  private readonly router = inject(Router);

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

  // The action bar's forward button: save, then let the wizard advance to step 3.
  protected onSaveDraft(): void {
    this.save((detail) => this.saved.emit(detail));
  }

  // T125: the centre "Guardar borrador" — same PUT, but the organizer leaves the wizard instead of
  // advancing. Mirrors basics-step/categories-step so the button means one thing in every step.
  protected onSaveAndLeave(): void {
    this.save(() => this.router.navigateByUrl('/organizer/dashboard'));
  }

  private save(onSuccess: (detail: CompetitionDetail) => void): void {
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
        onSuccess(detail);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.apiError.set(toGenericApiError(error));
      },
    });
  }
}
