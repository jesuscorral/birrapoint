import { Component, Input, Output, EventEmitter, forwardRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

@Component({
  selector: 'bp-checkbox',
  standalone: true,
  imports: [CommonModule],
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
  @Input() checked = false;
  @Input() disabled = false;
  @Output() change = new EventEmitter<boolean>();

  private onChangeCallback: (value: boolean) => void = () => {};
  private onTouchedCallback: () => void = () => {};

  onChange(event: Event): void {
    const target = event.target as HTMLInputElement;
    this.checked = target.checked;
    this.change.emit(this.checked);
    this.onChangeCallback(this.checked);
  }

  onTouched(): void {
    this.onTouchedCallback();
  }

  // ControlValueAccessor methods
  writeValue(value: boolean): void {
    this.checked = value || false;
  }

  registerOnChange(fn: (value: boolean) => void): void {
    this.onChangeCallback = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouchedCallback = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
  }
}
