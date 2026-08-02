import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  Input,
  forwardRef,
  inject,
  output,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

@Component({
  selector: 'bp-checkbox',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label class="check" [class.check-disabled]="disabled">
      <input
        type="checkbox"
        [checked]="checked"
        [disabled]="disabled"
        (change)="onChange($event)"
        (blur)="onTouched()"
      />
      <span>
        <ng-content></ng-content>
      </span>
    </label>
  `,
  styles: [
    `
      .check {
        display: flex;
        gap: var(--spacing-3);
        align-items: flex-start;
        font-size: 0.875rem;
        color: var(--color-bp-text-muted);
        cursor: pointer;
      }

      .check.check-disabled {
        cursor: not-allowed;
        opacity: 0.6;
      }

      .check input {
        flex: none;
        width: 20px;
        height: 20px;
        margin: 2px 0 0;
        accent-color: var(--color-bp-cobre-500);
        cursor: pointer;
      }

      .check.check-disabled input {
        cursor: not-allowed;
      }

      .check input:focus-visible {
        outline: 2px solid var(--color-bp-cobre-500);
        outline-offset: 2px;
        border-radius: 4px;
      }

      .check a {
        color: var(--color-bp-cobre-600);
        font-weight: 600;
        text-decoration: none;
      }

      .check a:hover {
        text-decoration: underline;
      }

      .check a:focus-visible {
        outline: 2px solid var(--color-bp-cobre-500);
        outline-offset: 2px;
      }
    `,
  ],
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => BpCheckboxComponent),
      multi: true,
    },
  ],
})
export class BpCheckboxComponent implements ControlValueAccessor {
  private readonly cdr = inject(ChangeDetectorRef);

  // Written imperatively by ControlValueAccessor.writeValue()/setDisabledState() (Reactive Forms
  // integration) — cannot be a signal input(), which is read-only from outside the component.
  @Input() checked = false;
  @Input() disabled = false;
  // Named checkedChange (not `checked`, which would collide with the Input above) to mirror the
  // valueChange convention already used by bp-input/bp-textarea, and to avoid no-output-native
  // (a bare `change` collides with the native DOM event name).
  readonly checkedChange = output<boolean>();

  // eslint-disable-next-line @typescript-eslint/no-empty-function
  private onChangeCallback: (value: boolean) => void = () => {};
  // eslint-disable-next-line @typescript-eslint/no-empty-function
  private onTouchedCallback: () => void = () => {};

  onChange(event: Event): void {
    const target = event.target as HTMLInputElement;
    this.checked = target.checked;
    this.checkedChange.emit(this.checked);
    this.onChangeCallback(this.checked);
  }

  onTouched(): void {
    this.onTouchedCallback();
  }

  // ControlValueAccessor methods
  writeValue(value: boolean): void {
    this.checked = value || false;
    // Called by Angular's forms directives from outside this OnPush component's own change
    // detection (e.g. FormGroup.patchValue()) — without this, the new value would never render.
    this.cdr.markForCheck();
  }

  registerOnChange(fn: (value: boolean) => void): void {
    this.onChangeCallback = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouchedCallback = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
    this.cdr.markForCheck();
  }
}
