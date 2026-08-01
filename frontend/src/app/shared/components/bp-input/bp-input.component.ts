import {
  Component,
  Input,
  Output,
  EventEmitter,
  ViewChild,
  ElementRef,
  forwardRef,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

@Component({
  selector: 'bp-input',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="field">
      <label *ngIf="label" class="field__label" [for]="id">
        {{ label }}
        <span *ngIf="required" class="req" aria-hidden="true">*</span>
      </label>
      <div class="field__control">
        <input
          #input
          [class]="inputClasses"
          [type]="showPassword ? 'text' : type"
          [id]="id"
          [placeholder]="placeholder"
          [required]="required"
          [disabled]="disabled"
          [value]="value"
          [attr.min]="min"
          [attr.max]="max"
          (input)="onInput($event)"
          (blur)="onBlur()"
          [attr.aria-invalid]="hasError"
          [attr.aria-describedby]="hint ? id + '-hint' : hasError ? id + '-error' : null"
        />
        <button
          *ngIf="type === 'password'"
          class="field__action"
          type="button"
          (click)="togglePassword()"
          [attr.aria-pressed]="showPassword"
        >
          {{ showPassword ? 'Ocultar' : 'Mostrar' }}
        </button>
      </div>
      <span *ngIf="hint && !hasError" class="field__hint" [id]="id + '-hint'">
        {{ hint }}
      </span>
      <span *ngIf="hasError && errorMessage" class="field__error" [id]="id + '-error'">
        <svg
          class="error-icon"
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2.5"
          stroke-linecap="round"
          aria-hidden="true"
        >
          <circle cx="12" cy="12" r="9" />
          <path d="M12 7v6M12 16.5v.01" />
        </svg>
        {{ errorMessage }}
      </span>
    </div>
  `,
  styles: [
    `
      .field {
        margin-bottom: var(--spacing-5);
      }

      .field__label {
        display: block;
        font-size: 0.875rem;
        font-weight: 600;
        color: var(--color-bp-text);
        margin-bottom: var(--spacing-2);
      }

      .req {
        color: var(--color-bp-danger-600);
        margin-left: 2px;
      }

      .field__control {
        position: relative;
        display: flex;
        align-items: center;
      }

      input {
        width: 100%;
        min-height: 44px;
        padding: 0 var(--spacing-4);
        font: inherit;
        font-size: 1rem;
        color: var(--color-bp-text);
        background: var(--color-bp-surface);
        border: 1.5px solid var(--color-bp-border-strong);
        border-radius: var(--radius-md);
        transition:
          border-color 0.15s ease,
          box-shadow 0.15s ease;
      }

      input::placeholder {
        color: var(--color-bp-text-subtle);
      }

      input:hover {
        border-color: var(--color-bp-verde-400);
      }

      input:focus {
        outline: none;
        border-color: var(--color-bp-cobre-500);
        box-shadow: 0 0 0 3px rgba(226, 162, 119, 0.2);
      }

      input[aria-invalid='true'] {
        border-color: var(--color-bp-danger-600);
        background: var(--color-bp-danger-50);
      }

      input[aria-invalid='true']:focus {
        box-shadow: 0 0 0 3px rgba(163, 39, 31, 0.18);
      }

      input:disabled {
        background: var(--color-bp-hueso-100);
        color: var(--color-bp-text-subtle);
        cursor: not-allowed;
      }

      input.with-action {
        padding-right: 3.25rem;
      }

      .field__action {
        position: absolute;
        right: 6px;
        height: 34px;
        min-height: 0;
        padding: 0 var(--spacing-3);
        font-size: 0.75rem;
        font-weight: 600;
        color: var(--color-bp-text-muted);
        background: transparent;
        border: 0;
        border-radius: var(--radius-sm);
        cursor: pointer;
      }

      .field__action:hover {
        background: var(--color-bp-hueso-100);
        color: var(--color-bp-text);
      }

      .field__action:focus-visible {
        outline: 2px solid var(--color-bp-cobre-500);
        outline-offset: 2px;
      }

      .field__hint {
        display: block;
        margin-top: var(--spacing-2);
        font-size: 0.875rem;
        color: var(--color-bp-text-muted);
      }

      .field__error {
        display: flex;
        align-items: flex-start;
        gap: 6px;
        margin-top: var(--spacing-2);
        font-size: 0.875rem;
        font-weight: 500;
        color: var(--color-bp-danger-600);
      }

      .error-icon {
        flex: none;
        margin-top: 3px;
      }
    `,
  ],
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => BpInputComponent),
      multi: true,
    },
  ],
})
export class BpInputComponent implements ControlValueAccessor {
  @ViewChild('input') inputElement!: ElementRef<HTMLInputElement>;

  @Input() id = 'bp-input-' + Math.random().toString(36).substr(2, 9);
  @Input() label = '';
  @Input() type: 'text' | 'email' | 'password' | 'tel' | 'url' | 'date' | 'number' = 'text';
  @Input() placeholder = '';
  @Input() required = false;
  @Input() disabled = false;
  @Input() hint = '';
  @Input() hasError = false;
  @Input() errorMessage = '';
  @Input() min: number | null = null;
  @Input() max: number | null = null;
  @Input() value = '';

  @Output() valueChange = new EventEmitter<string>();

  showPassword = false;

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  onInput(event: Event): void {
    const target = event.target as HTMLInputElement;
    this.value = target.value;
    this.valueChange.emit(this.value);
    this.onChange(this.value);
  }

  onBlur(): void {
    this.onTouched();
  }

  togglePassword(): void {
    this.showPassword = !this.showPassword;
    // Focus back on input after toggling
    setTimeout(() => {
      this.inputElement.nativeElement.focus();
    }, 0);
  }

  get inputClasses(): string {
    const base =
      'w-full min-h-11 px-4 font-base text-base rounded-md bg-bp-surface border-bp-border-strong border-1.5 text-bp-text placeholder:text-bp-text-subtle hover:border-bp-verde-400 focus:outline-none focus:border-bp-cobre-500 focus:ring-3 focus:ring-bp-cobre-100 disabled:bg-bp-hueso-100 disabled:text-bp-text-subtle disabled:cursor-not-allowed transition-colors duration-150 ease-out';
    const actionClass = this.type === 'password' ? 'with-action' : '';
    const errorClass = this.hasError ? 'border-bp-danger-600 bg-bp-danger-50' : '';

    return `${base} ${actionClass} ${errorClass}`.trim();
  }

  // ControlValueAccessor methods
  // Accepts number too (entryLimit etc. are FormControl<number|null>) — `value || ''` would drop
  // a legitimate 0, so compare against null/undefined explicitly instead of relying on falsiness.
  writeValue(value: string | number | null | undefined): void {
    this.value = value === null || value === undefined ? '' : String(value);
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
  }
}
