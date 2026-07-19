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
import { CompetitionsApiService } from '../competitions-api.service';
import type { CompetitionDetail, CompetitionPayload } from '../competitions-api.service';

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
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="onNext()">
      <label>
        Name
        <input type="text" formControlName="name" />
      </label>
      @if (fieldError('name'); as message) {
        <p role="alert">{{ message }}</p>
      }

      <label>
        Venue
        <input type="text" formControlName="venue" />
      </label>
      @if (fieldError('venue'); as message) {
        <p role="alert">{{ message }}</p>
      }

      <label>
        Start date
        <input type="date" formControlName="startDate" />
      </label>
      @if (fieldError('startDate'); as message) {
        <p role="alert">{{ message }}</p>
      }

      <label>
        End date
        <input type="date" formControlName="endDate" />
      </label>
      @if (fieldError('endDate'); as message) {
        <p role="alert">{{ message }}</p>
      }
      @if (form.errors?.['endBeforeStart']) {
        <p role="alert">End date must be on or after the start date.</p>
      }

      @if (bannerError(); as message) {
        <p role="alert">{{ message }}</p>
      }

      <button type="submit" [disabled]="form.invalid || submitting()">Next</button>
    </form>
  `,
})
export class BasicsStepComponent {
  private readonly api = inject(CompetitionsApiService);

  readonly competitionId = input<string | null>(null);
  readonly initialValue = input<CompetitionDetail | null>(null);
  readonly saved = output<CompetitionDetail>();

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
}
