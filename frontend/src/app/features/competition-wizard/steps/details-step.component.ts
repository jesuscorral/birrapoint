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

const SAVED_CONFIRMATION_MS = 3000;

@Component({
  selector: 'app-details-step',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="onSaveDraft()">
      <label>
        Description
        <textarea formControlName="description"></textarea>
      </label>
      @if (fieldError('description'); as message) {
        <p role="alert">{{ message }}</p>
      }

      <label>
        Logo URL
        <input type="text" formControlName="logoUrl" />
      </label>
      @if (fieldError('logoUrl'); as message) {
        <p role="alert">{{ message }}</p>
      }

      <label>
        Entry limit
        <input type="number" formControlName="entryLimit" />
      </label>
      @if (fieldError('entryLimit'); as message) {
        <p role="alert">{{ message }}</p>
      }
      @if (form.controls.entryLimit.errors?.['min']) {
        <p role="alert">Entry limit must be greater than zero.</p>
      }

      <label>
        Registration start
        <input type="date" formControlName="registrationStart" />
      </label>
      @if (fieldError('registrationStart'); as message) {
        <p role="alert">{{ message }}</p>
      }

      <label>
        Registration end
        <input type="date" formControlName="registrationEnd" />
      </label>
      @if (fieldError('registrationEnd'); as message) {
        <p role="alert">{{ message }}</p>
      }
      @if (form.errors?.['registrationEndBeforeStart']) {
        <p role="alert">Registration end must be on or after registration start.</p>
      }

      @if (bannerError(); as message) {
        <p role="alert">{{ message }}</p>
      }
      @if (savedConfirmation()) {
        <p>Saved</p>
      }

      <button type="submit" [disabled]="form.invalid || submitting()">Save Draft</button>
    </form>
  `,
})
export class DetailsStepComponent {
  private readonly api = inject(CompetitionsApiService);

  readonly competitionId = input.required<string>();
  readonly initialValue = input<CompetitionDetail | null>(null);
  readonly saved = output<CompetitionDetail>();

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
  protected readonly savedConfirmation = signal(false);

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
    this.savedConfirmation.set(false);

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
        this.savedConfirmation.set(true);
        this.saved.emit(detail);
        setTimeout(() => this.savedConfirmation.set(false), SAVED_CONFIRMATION_MS);
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.apiError.set(toGenericApiError(error));
      },
    });
  }
}
