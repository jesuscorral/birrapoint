import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  Input,
  computed,
  forwardRef,
  inject,
  input,
  output,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

@Component({
  selector: 'bp-textarea',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Same fix as bp-input.component.ts: a static id="x" attribute on <bp-textarea> would
  // otherwise also land on this host element (Angular reflects static attributes onto the host
  // in addition to feeding a matching signal input), duplicating the id the inner <textarea>
  // needs for its own label[for] association.
  host: { '[attr.id]': 'null' },
  template: `
    <div class="field">
      @if (label()) {
        <label class="field__label" [for]="id()">
          {{ label() }}
          @if (required()) {
            <span class="req" aria-hidden="true">*</span>
          }
        </label>
      }
      <div class="field__control">
        <textarea
          [class]="textareaClasses()"
          [id]="id()"
          [placeholder]="placeholder()"
          [required]="required()"
          [disabled]="disabled"
          [rows]="rows()"
          [value]="value"
          (input)="onInput($event)"
          (blur)="onBlur()"
          [attr.aria-invalid]="hasError()"
          [attr.aria-describedby]="hint() ? id() + '-hint' : hasError() ? id() + '-error' : null"
        ></textarea>
      </div>
      @if (hint() && !hasError()) {
        <span class="field__hint" [id]="id() + '-hint'">
          {{ hint() }}
        </span>
      }
      @if (hasError() && errorMessage()) {
        <span class="field__error" [id]="id() + '-error'">
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
          {{ errorMessage() }}
        </span>
      }
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
      }

      textarea {
        width: 100%;
        padding: var(--spacing-3) var(--spacing-4);
        font: inherit;
        font-size: 1rem;
        line-height: 1.5;
        color: var(--color-bp-text);
        background: var(--color-bp-surface);
        border: 1.5px solid var(--color-bp-border-strong);
        border-radius: var(--radius-md);
        resize: vertical;
        transition:
          border-color 0.15s ease,
          box-shadow 0.15s ease;
      }

      textarea::placeholder {
        color: var(--color-bp-text-subtle);
      }

      textarea:hover {
        border-color: var(--color-bp-verde-400);
      }

      textarea:focus {
        outline: none;
        border-color: var(--color-bp-cobre-500);
        box-shadow: 0 0 0 3px rgba(226, 162, 119, 0.2);
      }

      textarea[aria-invalid='true'] {
        border-color: var(--color-bp-danger-600);
        background: var(--color-bp-danger-50);
      }

      textarea[aria-invalid='true']:focus {
        box-shadow: 0 0 0 3px rgba(163, 39, 31, 0.18);
      }

      textarea:disabled {
        background: var(--color-bp-hueso-100);
        color: var(--color-bp-text-subtle);
        cursor: not-allowed;
        resize: none;
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
      useExisting: forwardRef(() => BpTextareaComponent),
      multi: true,
    },
  ],
})
export class BpTextareaComponent implements ControlValueAccessor {
  private readonly cdr = inject(ChangeDetectorRef);

  readonly id = input('bp-textarea-' + Math.random().toString(36).slice(2, 11));
  readonly label = input('');
  readonly placeholder = input('');
  readonly required = input(false);
  readonly hint = input('');
  readonly hasError = input(false);
  readonly errorMessage = input('');
  readonly rows = input(4);

  // Written imperatively by ControlValueAccessor.writeValue()/setDisabledState() (Reactive Forms
  // integration) — cannot be a signal input(), which is read-only from outside the component.
  @Input() value = '';
  @Input() disabled = false;

  readonly valueChange = output<string>();

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  onInput(event: Event): void {
    const target = event.target as HTMLTextAreaElement;
    this.value = target.value;
    this.valueChange.emit(this.value);
    this.onChange(this.value);
  }

  onBlur(): void {
    this.onTouched();
  }

  protected readonly textareaClasses = computed(() => {
    const base =
      'w-full font-base text-base rounded-md bg-bp-surface border-bp-border-strong border-1.5 text-bp-text placeholder:text-bp-text-subtle hover:border-bp-verde-400 focus:outline-none focus:border-bp-cobre-500 focus:ring-3 focus:ring-bp-cobre-100 disabled:bg-bp-hueso-100 disabled:text-bp-text-subtle disabled:cursor-not-allowed transition-colors duration-150 ease-out';
    const errorClass = this.hasError() ? 'border-bp-danger-600 bg-bp-danger-50' : '';

    return `${base} ${errorClass}`.trim();
  });

  // ControlValueAccessor methods
  writeValue(value: string | null | undefined): void {
    this.value = value ?? '';
    // Called by Angular's forms directives from outside this OnPush component's own change
    // detection (e.g. FormGroup.patchValue()) — without this, the new value would never render.
    this.cdr.markForCheck();
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
    this.cdr.markForCheck();
  }
}
