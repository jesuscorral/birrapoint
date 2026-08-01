import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'bp-alert',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div [class]="alertClasses" [attr.role]="role">
      <svg
        *ngIf="icon"
        class="alert__icon"
        [innerHTML]="icon"
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2.2"
        stroke-linecap="round"
        aria-hidden="true"
      ></svg>
      <span>
        <strong *ngIf="title" class="alert-title">{{ title }}</strong>
        <ng-content></ng-content>
      </span>
    </div>
  `,
  styles: [
    `
      .alert {
        display: flex;
        gap: var(--spacing-3);
        padding: var(--spacing-4);
        border-radius: var(--radius-md);
        border: 1px solid;
        font-size: 0.875rem;
        margin-bottom: var(--spacing-6);
      }

      .alert__icon {
        flex: none;
        margin-top: 1px;
      }

      .alert-title {
        display: block;
        font-weight: 700;
        margin-bottom: 2px;
      }

      .alert-error {
        background: var(--color-bp-danger-50);
        border-color: #efc9c5;
        color: #7b1e17;
      }

      .alert-info {
        background: var(--color-bp-info-50);
        border-color: #c4d8e2;
        color: #22536b;
      }

      .alert-success {
        background: var(--color-bp-exito-50);
        border-color: #c6ddd0;
        color: #245a3b;
      }
    `,
  ],
})
export class BpAlertComponent {
  @Input() type: 'error' | 'info' | 'success' = 'info';
  @Input() title = '';
  @Input() role: 'alert' | 'status' = 'alert';

  get alertClasses(): string {
    const base = 'alert';
    const typeClass = `alert-${this.type}`;
    return `${base} ${typeClass}`;
  }

  get icon(): string {
    switch (this.type) {
      case 'error':
        return '<circle cx="12" cy="12" r="9" /><path d="M12 7v6M12 16.5v.01" />';
      case 'success':
        return '<path d="M20 6 9 17l-5-5" />';
      case 'info':
      default:
        return '<circle cx="12" cy="12" r="9" /><path d="M12 16v-5M12 8v.01" />';
    }
  }
}
